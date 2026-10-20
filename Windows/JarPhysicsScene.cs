using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;
using Box2D.NetStandard.Dynamics.Joints.Mouse;
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.World.Callbacks;
using Box2D.NetStandard.Dynamics.Contacts;
using NVector=System.Numerics.Vector2;

namespace PetJar;

// The renderer, pointer hit testing and eyes all consume this one body pose.
// Units are metres in Box2D and DIPs at the native window boundary.
internal sealed class JarObject
{
    internal required string Id {get;init;}
    internal required string Sprite {get;init;}
    internal Client? Client {get;init;}
    internal required Body Body {get;init;}
    internal required Size Size {get;init;}
    internal required MaterialSquash Squash {get;init;}
    // Two depth slices share gravity and glass but collide within their slice.
    // Every rear item is a real, independently interactive body.
    internal int Depth {get;init;}
    internal double Softness=>Squash.Limit;
    internal double Compression=>Squash.Compression;
    internal NVector SafePosition;
    internal float SafeAngle;
    internal Point Center {get {var p=Body.GetPosition();return new(p.X*JarPhysicsScene.PixelsPerMetre,p.Y*JarPhysicsScene.PixelsPerMetre);}}
    internal Matrix Pose {
        get {
            var m=Squash.Transform;
            m.Rotate(Body.GetAngle()*180/Math.PI);var center=Center;m.Translate(center.X,center.Y);return m;
        }
    }
    internal Rect LocalBounds=>new(-Size.Width/2,-Size.Height/2,Size.Width,Size.Height);
    internal Rect Bounds=>new MatrixTransform(Pose).TransformBounds(LocalBounds);
    internal bool Contains(Point point) {
        var inverse=Pose;inverse.Invert();var local=inverse.Transform(point);
        return Art.SpriteContains(Sprite,new(local.X/Size.Width+.5,local.Y/Size.Height+.5));
    }
    internal Vector LocalNormal(NVector normal) {
        double angle=-Body.GetAngle(),c=Math.Cos(angle),s=Math.Sin(angle);
        return new(normal.X*c-normal.Y*s,normal.X*s+normal.Y*c);
    }
    internal Vector GripNormal(Point point) {
        var inverse=Pose;inverse.Invert();var p=inverse.Transform(point);return new(p.X,p.Y);
    }
}

internal sealed class JarPhysicsScene
{
    internal const float PixelsPerMetre=50;
    internal const int BasePropCount=26;
    internal const int InfillCount=28;
    internal const int FrontPropCount=BasePropCount+InfillCount;
    internal int FillerCount=>objects.Count(o=>o.Client==null);
    private const float StepSeconds=1f/120;
    private readonly World world=new(new NVector(0,10));
    private readonly Body wall;
    private readonly List<JarObject> objects=new();
    private readonly Impacts impacts;
    private MouseJoint? mouse;
    private JarObject? grabbed;
    private JarObject? pressed;
    private double accumulator;
    internal IReadOnlyList<JarObject> Objects=>objects;
    internal JarObject? Grabbed=>grabbed;
    internal JarObject? Pressed=>pressed;
    internal int CollisionCount {get;private set;}
    internal int JarMotionImpulseCount {get;private set;}
    internal int ContactCount=>world.GetContactCount();
    internal bool IsAwake=>grabbed!=null||objects.Any(o=>o.Body.IsAwake()||o.Squash.IsChanging);
    internal IReadOnlyList<FillerKind> SelectedFillers {get;}
    internal JarPhysicsScene(IEnumerable<Client> clients,IEnumerable<FillerKind>? fillers=null)
    {
        SelectedFillers=(fillers??Enum.GetValues<FillerKind>()).Where(Enum.IsDefined).Distinct().OrderBy(kind=>kind).ToArray();
        wall=world.CreateBody(new BodyDef());
        // Flatten the actual glass interior, including its transform. There is
        // no second hand-maintained rectangular wall or invisible bottom line.
        var path=JarGeometry.Interior.GetFlattenedPathGeometry(.45,ToleranceType.Absolute);
        foreach(var figure in path.Figures) {
            var points=new List<Point> {path.Transform?.Transform(figure.StartPoint)??figure.StartPoint};
            foreach(var segment in figure.Segments) {
                if(segment is PolyLineSegment poly)foreach(var point in poly.Points)points.Add(path.Transform?.Transform(point)??point);
                else if(segment is LineSegment line)points.Add(path.Transform?.Transform(line.Point)??line.Point);
            }
            double area=0;for(int i=0;i<points.Count;i++) {var a=points[i];var b=points[(i+1)%points.Count];area+=a.X*b.Y-b.X*a.Y;}
            for(int i=0;i<points.Count;i++) {
                var a=WorldPoint(points[i]);var b=WorldPoint(points[(i+1)%points.Count]);
                if(NVector.DistanceSquared(a,b)<.00001f)continue;
                // Solid bands extend OUTWARD from the exact inner contour.
                // Unlike zero-thickness edges they also contain the pressure
                // caused by returning several pets to an already packed jar.
                var delta=b-a;float length=delta.Length();
                var outward=new NVector(delta.Y,-delta.X)/length*(area>0?1:-1);
                const float thickness=.7f;var center=(a+b)*.5f+outward*(thickness*.5f);
                var band=new PolygonShape();band.SetAsBox(length*.5f+.012f,thickness*.5f,in center,MathF.Atan2(delta.Y,delta.X));
                var fixture=new FixtureDef {shape=band,friction=.5f,restitution=.08f,userData=outward};wall.CreateFixture(in fixture);
            }
        }
        impacts=new(this);world.SetContactListener(impacts);
        var materials=SelectedFillers.Select(kind=>kind.ToString().ToLowerInvariant()).ToArray();
        // Independent source assets replace the formerly baked pile. Start in
        // loose layers and let the same solver used at runtime pack the jar.
        for(int i=0;materials.Length>0&&i<BasePropCount;i++) {
            string material=materials[i%materials.Length];
            double size=material=="bubble"?23:25+(i%4)*2;
            Point point=i<8?new(43+i*24,202):i<16?new(42+(i-8)*24,24):
                new(i%2==0?40:218,52+(i-16)/2*21);
            Add("prop-"+i,"prop-"+material,null,point,size,size,i%3*.08f-.08f);
        }
        // Reserve space for every pet before infilling, including pets currently
        // in the showcase. Adding them later must not overfill the container.
        foreach(var client in Enum.GetValues<Client>())AddPet(client,false);
        for(int i=0;i<480;i++)Step();
        if(materials.Length>0)FillGaps(materials);
        if(materials.Length>0)FillDepth(materials);
        // Small pieces read as further back; the same order drives alpha hits.
        objects.Sort((a,b)=> {
            int depth=a.Depth.CompareTo(b.Depth);if(depth!=0)return depth;
            int layer=(a.Client!=null?1:0).CompareTo(b.Client!=null?1:0);
            return layer!=0?layer:(a.Size.Width*a.Size.Height).CompareTo(b.Size.Width*b.Size.Height);
        });
        SetPets(clients);
        CollisionCount=0;
    }
    private void FillDepth(string[] materials)
    {
        // Fill by physical cross-section, so round, star and block selections
        // reach a similar level instead of relying on one fixed body count.
        double area=0,target=JarGeometry.Interior.GetArea()*.68/(PixelsPerMetre*PixelsPerMetre);
        for(int index=0;area<target&&index<160;index++) {
            string material=materials[(FrontPropCount+index)%materials.Length];
            double size=26+(index%4)*2;
            var position=new Point(43+index%8*24+(index/8%2==0?-3:3),217-index/8%12*15);
            if(!JarGeometry.Interior.FillContains(position))position=new(Math.Clamp(position.X,50,210),Math.Clamp(position.Y,50,210));
            var item=Add("rear-"+index,"prop-"+material,null,position,size,size,(index%7-3)*.14f,-1);
            area+=item.Body.GetMass()/Density(null,item.Sprite);
        }
        for(int step=0;step<720;step++)Step();
    }
    private void FillGaps(string[] materials)
    {
        const int pitch=3,columns=87,rows=76;
        for(int index=0;index<InfillCount;index++) {
            var distance=new double[columns*rows];
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++) {
                var point=new Point(x*pitch,y*pitch);
                bool occupied=!JarGeometry.Interior.FillContains(point)||objects.Any(o=>o.Contains(point));
                distance[y*columns+x]=occupied?0:1000;
            }
            // Chamfer distance finds the largest visible cavity, rather than
            // adding another uniform row against the sides of the glass.
            for(int y=1;y<rows;y++)for(int x=1;x<columns-1;x++) {
                int p=y*columns+x;distance[p]=Math.Min(distance[p],Math.Min(Math.Min(distance[p-1]+1,distance[p-columns]+1),Math.Min(distance[p-columns-1],distance[p-columns+1])+1.414));
            }
            for(int y=rows-2;y>=0;y--)for(int x=columns-2;x>0;x--) {
                int p=y*columns+x;distance[p]=Math.Min(distance[p],Math.Min(Math.Min(distance[p+1]+1,distance[p+columns]+1),Math.Min(distance[p+columns-1],distance[p+columns+1])+1.414));
            }
            double Score(int i)=>distance[i]*(i/columns*pitch>80?2.8:1);
            int best=0;for(int i=1;i<distance.Length;i++)if(Score(i)>Score(best))best=i;
            double size=Math.Clamp(Math.Floor(distance[best]*pitch*1.60),12,22);
            var position=new Point(best%columns*pitch,best/columns*pitch);
            Add("infill-"+index,"prop-"+materials[(BasePropCount+index)%materials.Length],null,position,size,size,(index%5-2)*.17f);
            for(int step=0;step<48;step++)Step();
        }
        for(int step=0;step<360;step++)Step();
    }
    private static NVector WorldPoint(Point point)=>new((float)point.X/PixelsPerMetre,(float)point.Y/PixelsPerMetre);
    private static bool InsideGlass(Point p)=>
        (p.X>=34&&p.X<=226&&p.Y>=24&&p.Y<=204)||JarGeometry.Interior.FillContains(p);
    internal JarObject? Hit(Point point) {
        if(!JarGeometry.Interior.FillContains(point))return null;
        for(int i=objects.Count-1;i>=0;i--)if(objects[i].Contains(point))return objects[i];
        return null;
    }
    internal void SetPets(IEnumerable<Client> clients)
    {
        var next=new HashSet<Client>(clients);
        if(next.SetEquals(objects.Where(o=>o.Client!=null).Select(o=>o.Client!.Value)))return;
        foreach(var item in objects.Where(o=>o.Client is Client c&&!next.Contains(c)).ToArray()) {
            if(grabbed==item||pressed==item)Release();impacts.Forget(item);world.DestroyBody(item.Body);objects.Remove(item);
        }
        foreach(var client in next)if(!objects.Any(o=>o.Client==client))AddPet(client,true);
        foreach(var item in objects)item.Body.SetAwake(true);
    }
    private void AddPet(Client client,bool drop)
    {
        var (point,width,height)=client switch {
            Client.ChatGPT=>(new Point(82,64),74d,70d),
            Client.Grok=>(new Point(178,106),65d,65d),
            Client.Muse=>(new Point(78,147),65d,73d),
            _=>(new Point(172,175),75d,45d)
        };
        if(drop)point=new(100+(int)client*16,30);
        Add("pet-"+client.ToString().ToLowerInvariant(),client.Sprite(),client,point,width,height,0);
    }
    private static float Density(Client? client,string sprite)=>sprite.Contains("chrome")?1.5f:sprite.Contains("bubble")?.18f:client==Client.Grok?1.2f:.75f;
    private JarObject Add(string id,string sprite,Client? client,Point point,double width,double height,float angle,int depth=0)
    {
        var definition=new BodyDef {type=BodyType.Dynamic,position=WorldPoint(point),angle=angle,linearDamping=depth<0?.9f:.18f,angularDamping=depth<0?2.8f:1.6f,bullet=true,allowSleep=true,awake=true};
        var body=world.CreateBody(definition);
        var item=new JarObject {Id=id,Sprite=sprite,Client=client,Body=body,Size=new(width,height),Depth=depth,Squash=new(SquashMaterial.For(client,sprite)),SafePosition=definition.position,SafeAngle=angle};
        body.SetUserData(item);objects.Add(item);
        // A small art overhang creates front/back occlusion at contact while
        // leaving room for the pile to rearrange, rather than locking an arch.
        float fit=client==null?(depth<0?.74f:.88f):.97f;
        float w=(float)width/PixelsPerMetre*fit,h=(float)height/PixelsPerMetre*fit;
        float density=Density(client,sprite);
        void Fixture(Box2D.NetStandard.Collision.Shapes.Shape shape) {
            var fixture=new FixtureDef {shape=shape,density=density,friction=depth<0?.34f:.48f,restitution=depth<0?.03f:sprite.Contains("bubble")?.42f:.13f,
                filter=new Filter {categoryBits=(ushort)(depth<0?4:2),maskBits=(ushort)(depth<0?5:3)}};body.CreateFixture(in fixture);
        }
        void Circle(float radius,float x=0,float y=0)=>Fixture(new CircleShape {Radius=radius,Center=new NVector(x,y)});
        void Polygon(params NVector[] points) {var polygon=new PolygonShape();polygon.Set(in points);Fixture(polygon);}
        if(client==Client.ChatGPT)Polygon(new(-w*.04f,-h*.48f),new(w*.18f,-h*.43f),new(w*.49f,h*.29f),new(w*.37f,h*.44f),new(-w*.38f,h*.44f),new(-w*.49f,h*.28f),new(-w*.22f,-h*.34f));
        else if(client==Client.Muse) {
            Polygon(new(-w*.29f,-h*.33f),new(w*.29f,-h*.33f),new(w*.40f,h*.32f),new(w*.29f,h*.48f),new(-w*.29f,h*.48f),new(-w*.40f,h*.32f));Circle(w*.34f,0,-h*.16f);
        }
        else if(client==Client.Claude) {
            Circle(h*.44f,0,h*.02f);Circle(h*.20f,-w*.36f,-h*.20f);Circle(h*.20f,w*.36f,-h*.20f);
            Polygon(new(-w*.30f,-h*.16f),new(w*.30f,-h*.16f),new(w*.34f,h*.26f),new(w*.23f,h*.43f),new(-w*.23f,h*.43f),new(-w*.34f,h*.26f));
        }
        else if(sprite.Contains("chrome")) {
            Circle(w*.23f);Circle(w*.17f,-w*.29f,0);Circle(w*.17f,w*.29f,0);Circle(h*.17f,0,-h*.29f);Circle(h*.17f,0,h*.29f);
        }
        else if(sprite.Contains("blue"))Polygon(new(-w*.05f,-h*.47f),new(w*.44f,h*.21f),new(w*.36f,h*.43f),new(w*.07f,h*.47f),new(-w*.46f,h*.22f),new(-w*.43f,-h*.05f));
        else if(client==Client.Grok||sprite.Contains("bubble")||sprite.Contains("moss"))Circle(Math.Min(w,h)*.48f);
        else {
            // Bevelled block keeps corners from looking as though they penetrate.
            float x=w*.46f,y=h*.46f,b=Math.Min(w,h)*.12f;
            Polygon(new(-x+b,-y),new(x-b,-y),new(x,-y+b),new(x,y-b),new(x-b,y),new(-x+b,y),new(-x,y-b),new(-x,-y+b));
        }
        return item;
    }
    internal void Advance(double seconds)
    {
        accumulator+=Math.Clamp(seconds,0,.06);
        while(accumulator>=StepSeconds) {Step();accumulator-=StepSeconds;}
    }
    internal void ApplyJarVelocityChange(Vector deltaPixelsPerSecond)
    {
        if(!double.IsFinite(deltaPixelsPerSecond.X)||!double.IsFinite(deltaPixelsPerSecond.Y)||deltaPixelsPerSecond.LengthSquared<.000001)return;
        var change=new NVector((float)-deltaPixelsPerSecond.X/PixelsPerMetre,(float)-deltaPixelsPerSecond.Y/PixelsPerMetre);
        foreach(var item in objects) {
            var impulse=change*item.Body.GetMass();item.Body.ApplyLinearImpulseToCenter(in impulse,true);
            var velocity=item.Body.GetLinearVelocity();
            if(velocity.Length()>12) {velocity=NVector.Normalize(velocity)*12;item.Body.SetLinearVelocity(in velocity);}
        }
        JarMotionImpulseCount++;
    }
    private void Step()
    {
        world.Step(StepSeconds,8,4);
        foreach(var item in objects)item.Squash.ClearLoad();
        impacts.ApplyLoads();
        foreach(var item in objects) {
            var position=item.Body.GetPosition();
            if(!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!InsideGlass(item.Center)) {
                // Dense contact islands can exhaust the solver's correction
                // budget. Reject only that crossing step; never relocate an
                // escaped object across the scene or let it fall off-screen.
                item.Body.SetTransform(in item.SafePosition,item.SafeAngle);
                var stopped=NVector.Zero;item.Body.SetLinearVelocity(in stopped);item.Body.SetAngularVelocity(0);
            }
            else {item.SafePosition=position;item.SafeAngle=item.Body.GetAngle();}
            item.Squash.Advance(StepSeconds);
        }
    }
    internal void Grab(JarObject item,Point point)
    {
        Release();if(!objects.Contains(item))return;
        mouse=(MouseJoint)world.CreateJoint(new MouseJointDef {bodyA=wall,bodyB=item.Body,Target=WorldPoint(point),MaxForce=item.Body.GetMass()*190,FrequencyHz=7,DampingRatio=.88f,collideConnected=true});
        grabbed=item;Press(item,point);item.Body.SetAwake(true);
    }
    internal void Press(JarObject item,Point point)
    {
        pressed?.Squash.Release();pressed=null;
        if(!objects.Contains(item))return;
        pressed=item;item.Squash.Hold(item.GripNormal(point));
    }
    internal void Drag(Point point)
    {
        mouse?.SetTarget(WorldPoint(new(Math.Clamp(point.X,24,236),Math.Clamp(point.Y,12,216))));
    }
    internal void Release(Vector? pointerVelocity=null)
    {
        pressed?.Squash.Release();pressed=null;
        if(mouse!=null) {world.DestroyJoint(mouse);mouse=null;}
        if(grabbed!=null) {
            var velocity=grabbed.Body.GetLinearVelocity();
            if(pointerVelocity is Vector v)velocity=velocity*.3f+new NVector((float)v.X/PixelsPerMetre,(float)v.Y/PixelsPerMetre)*.7f;
            if(velocity.Length()>7)velocity=NVector.Normalize(velocity)*7;
            grabbed.Body.SetLinearVelocity(in velocity);
            grabbed.Body.SetAngularVelocity(Math.Clamp(grabbed.Body.GetAngularVelocity(),-9,9));
        }
        grabbed=null;
    }
    internal void Poke(JarObject item,Point point)
    {
        if(!objects.Contains(item))return;
        var impulse=new NVector((float)(item.Center.X-point.X)*.018f,-.78f)*item.Body.GetMass();
        var at=WorldPoint(point);item.Body.ApplyLinearImpulse(in impulse,in at,true);
        item.Squash.Impact(item.GripNormal(point),1.7);
    }
    private sealed class Impacts(JarPhysicsScene scene):ContactListener
    {
        private readonly HashSet<Contact> began=new();
        private readonly Dictionary<Contact,Load> loads=new();
        private readonly record struct Load(JarObject? A,JarObject? B,NVector Normal,float Force);
        public override void BeginContact(in Contact contact) {scene.CollisionCount++;began.Add(contact);}
        public override void EndContact(in Contact contact) {began.Remove(contact);loads.Remove(contact);}
        public override void PreSolve(in Contact contact,in Box2D.NetStandard.Collision.Manifold manifold) { }
        public override void PostSolve(in Contact contact,in ContactImpulse impulse)
        {
            bool started=began.Remove(contact);
            var fa=contact.GetFixtureA();var fb=contact.GetFixtureB();
            var a=fa.GetBody().GetUserData<JarObject>();var b=fb.GetBody().GetUserData<JarObject>();
            if((a==null||a.Softness==0)&&(b==null||b.Softness==0))return;
            NVector normal=fa.UserData is NVector na?na:fb.UserData is NVector nb?nb:fb.GetBody().GetPosition()-fa.GetBody().GetPosition();
            if(normal.LengthSquared()<1e-8f)normal=NVector.UnitY;else normal=NVector.Normalize(normal);
            float strength=0;foreach(float value in impulse.normalImpulses)strength+=Math.Max(0,value);
            loads[contact]=new(a,b,normal,strength/StepSeconds);
            if(!started)return;
            foreach(var item in new[] {a,b})if(item!=null&&item.Softness>0) {
                float speed=strength/Math.Max(.02f,item.Body.GetMass());
                if(speed>.18f)item.Squash.Impact(item.LocalNormal(normal),speed);
            }
        }
        internal void ApplyLoads()
        {
            // Keep the last solved load for sleeping contacts, so plush does
            // not inflate merely because its supporting pile went to sleep.
            foreach(var load in loads.Values) {Apply(load.A,load);Apply(load.B,load);}
        }
        private static void Apply(JarObject? item,Load load) {
            if(item!=null&&item.Softness>0)item.Squash.AddLoad(item.LocalNormal(load.Normal),load.Force/Math.Max(.005,item.Body.GetMass()*10));
        }
        internal void Forget(JarObject item)
        {
            foreach(var key in loads.Where(p=>p.Value.A==item||p.Value.B==item).Select(p=>p.Key).ToArray()) {loads.Remove(key);began.Remove(key);}
        }
    }
}
