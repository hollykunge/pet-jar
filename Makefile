.PHONY: build run test package

build:
	bash ./build.sh

run: build
	open dist/宠物罐.app

test: build

package: build
	ditto -c -k --sequesterRsrc --keepParent dist/宠物罐.app dist/宠物罐-macOS.zip
