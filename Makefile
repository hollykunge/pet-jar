.PHONY: build run test package

ifeq ($(OS),Windows_NT)
build:
	powershell -NoProfile -File ./build.ps1

run:
	powershell -NoProfile -File ./build.ps1 -Run

test:
	powershell -NoProfile -File ./build.ps1

package:
	powershell -NoProfile -File ./build.ps1 -Package
else
build:
	bash ./build.sh

run: build
	open dist/宠物罐.app

test: build

package: build
	ditto -c -k --sequesterRsrc --keepParent dist/宠物罐.app dist/宠物罐-macOS.zip
endif
