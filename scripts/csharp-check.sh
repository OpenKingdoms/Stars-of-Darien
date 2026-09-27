#!/usr/bin/env bash
# Check the C# side without a Unity license: compile every script in
# unity/Assets/Scripts against the editor's UnityEngine assemblies, then
# run the EditMode test bodies in plain .NET against the built plugin.
# Uses the compiler and runtime that ship inside the Unity editor.
# Build the core first. Windows, from Git Bash.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
data="${UNITY_DATA:-/d/Unity/6000.3.25f1/Editor/Data}"
build="${OKCORE_BUILD:-/d/OKBuild/okcore}"
out="$build/csharp-check"
mkdir -p "$out"
w() { cygpath -w "$1"; }
dotnet="$data/NetCoreRuntime/dotnet.exe"
csc() { "$dotnet" "$(w "$data/DotNetSdkRoslyn/csc.dll")" -nologo -noconfig -nostdlib -langversion:9 "$@"; }
nunit="$data/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll"

engine=()
for f in "$data"/Managed/UnityEngine/UnityEngine*.dll; do engine+=("-r:$(w "$f")"); done
# Package runtimes (uGUI, URP and the rest), once the editor has built
# them. Editor-only and test assemblies, and the project's own, stay out.
for f in "$root"/unity/Library/ScriptAssemblies/*.dll; do
    case "$(basename "$f")" in
        OpenKingdomsUnity*|*Editor*|*editor*|*Tests*|*TestRunner*|*nunit*) ;;
        *) engine+=("-r:$(w "$f")") ;;
    esac
done
scripts=()
while IFS= read -r f; do scripts+=("$(w "$f")"); done < <(find "$root/unity/Assets/Scripts" "$root/unity/Assets/Engine" -name "*.cs" -not -path "*/Editor/*")
csc -target:library -warn:4 -out:"$(w "$out/OpenKingdomsUnity.dll")" \
    -r:"$(w "$data/NetStandard/ref/2.1.0/netstandard.dll")" "${engine[@]}" "${scripts[@]}"
echo "Unity scripts compile"

fw=$(ls -d "$data"/NetCoreRuntime/shared/Microsoft.NETCore.App/* | head -1)
refs=()
for f in "$fw"/*.dll; do
    case "$(basename "$f")" in
        *.Native.dll) ;;
        System.*|netstandard.dll|mscorlib.dll) refs+=("-r:$(w "$f")") ;;
    esac
done
csc -target:exe -nowarn:CS1701,CS1702 -out:"$(w "$out/runner.dll")" "${refs[@]}" -r:"$(w "$nunit")" \
    "$(w "$root/unity/Assets/Scripts/OkSim.cs")" \
    "$(w "$root/unity/Assets/Tests/Editor/OkSimTests.cs")" \
    "$(w "$root/scripts/csharp-check/Runner.cs")"
cp "$nunit" "$root/scripts/csharp-check/runner.runtimeconfig.json" "$out/"
cp "$build/Release/okcore.dll" "$out/"
cd "$out" && "$dotnet" runner.dll
