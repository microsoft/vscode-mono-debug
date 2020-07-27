
$cmd = $args[0];

if (-Not $cmd)
{
	$cmd = "debug"
}

switch ($args[0]) {
	"all" {
		Write-Host "vsix created"
	}
	"vsix" {
		& ./node_modules/.bin/vsce package
	}
	"publish" {
		& ./node_modules/.bin/vsce publish
	}
	"build" {
		& msbuild /r /p:Configuration=Release ./src/mono-debug/mono-debug.csproj
		& msbuild /r /p:Configuration=Release ./src/xamarin-util/xamarin-util.csproj

		& npx webpack --mode production ./
		& node_modules/.bin/tsc -p ./src/typescript

		Write-Host "build finished"
	}
	"debug" {
		& msbuild /r /p:Configuration=Debug ./src/mono-debug/mono-debug.csproj
		& msbuild /r /p:Configuration=Debug ./src/xamarin-util/xamarin-util.csproj

		& node_modules/.bin/tsc -p ./src/typescript
	}
}