
MONO_DEBUG_RELEASE = "./bin/Release/mono-debug.exe"
MONO_DEBUG_DEBUG = "./bin/Debug/mono-debug.exe"

all: vsix
	@echo "vsix created"

vsix: $MONO_DEBUG_RELEASE
	./node_modules/.bin/vsce package

publish: $MONO_DEBUG_RELEASE
	./node_modules/.bin/vsce publish

build: $MONO_DEBUG_RELEASE
	node_modules/.bin/tsc -p ./tests
	@echo "build finished"

debug: $MONO_DEBUG_DEBUG
	node_modules/.bin/tsc -p ./tests
	@echo "build finished"

$MONO_DEBUG_RELEASE:
	xbuild /p:Configuration=Release mono-debug.sln

$MONO_DEBUG_DEBUG:
	xbuild /p:Configuration=Debug mono-debug.sln

clean:
	git clean -xfd