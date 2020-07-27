
$cmd = $args[0];

if (-Not $cmd)
{
	$cmd = "debug"
}

function Vsix
{
	& ./node_modules/.bin/vsce package
	Write-Host "vsix created"
}
function Build
{
	& msbuild /r /p:Configuration=Release ./src/mono-debug/mono-debug.csproj
	& msbuild /r /p:Configuration=Release ./src/xamarin-util/xamarin-util.csproj

	& node_modules/.bin/tsc -p ./src/typescript

	Write-Host "build finished"
}

function Debug
{
	& msbuild /r /p:Configuration=Debug ./src/mono-debug/mono-debug.csproj
	& msbuild /r /p:Configuration=Debug ./src/xamarin-util/xamarin-util.csproj

	& node_modules/.bin/tsc -p ./src/typescript
	Write-Host "debug build finished"
}

switch ($cmd) {
	"all" {
		Debug
		Build
		Vsix
	}
	"vsix" {
		Build
		Vsix
	}
	"build" {
		Build
	}
	"debug" {
		Debug
	}
}