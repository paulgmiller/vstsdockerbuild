#Having a really hard time getting msbuild in the path. Installer doesn't do it and the dev console is pretty opaque.
#Ideally choco install microsoft-build-tools would do this but it actually failed on my machine and doesn't set up nuget.
#could also grab from https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/CloudBuild.Tools.MSBuild/426d48b2a4b650bb614e776e649ddddda155755d/664c2385-7712-b0fe-45e2-7a2dc670b91b?root=release/amd64/MSBuild
#src: https://mseng.visualstudio.com/Domino/CloudBuild/_git/CloudBuild.Tools.MsBuild/?path=%2Fsrc%2FMSBuildDeploy&version=GBmaster&_a=contents
#which exactly mimics cloudbuild https://c/depot?file=//depot/config/batmon/Q-Prod-Co3/Coordinator/ToolsReleaseConfig-GeneralPublic.json

function add-path([string] $p) {
    if (! $env:Path.EndsWith(';')) {$env:Path += ';' }
    $env:PATH += "$p;"
}

#cloudbuild wants msbuild in path not just an alias
if (!(Get-Command "msbuild.exe" -ErrorAction SilentlyContinue))
{
    #this is gross I hate installers vs and build tools install to different place. 
    $editions = "Enterprise", "Professional", "Community", "BuildTools"
    
    $msbuild15 = "none"
    foreach ($edition in $editions)
    {
        #didn't seem to actually work on my home machine perhaps build tools doesn't install enough need to try and reproduce
        $msbuild15 = join-path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2017\$edition\MSBuild\15.0\Bin\"
        if (test-path $msbuild15)
        {
            add-path $msbuild15;
            break;
        }
    }
    if (! (test-path $msbuild15))
    {
        #should we try and install if not there? Chould use chocolatey
        throw "Didn't find MSBuild at $msbuild15 or $vsmsbuild15. Install vs2017 or msbuild 15 (https://www.visualstudio.com/thank-you-downloading-visual-studio/?sku=BuildTools&rel=15#). choco install microsoft-build-tools also might work"
    }
    "Using msbuild at $msbuild15"
        
}

#so msbuild needs the nuget with a credential provider.
#https://docs.microsoft.com/en-us/vsts/package/nuget/nuget-exe
if (!(test-path ENV:NUGET_CREDENTIALPROVIDERS_PATH))
{
    $ENV:NUGET_CREDENTIALPROVIDERS_PATH = "$PSScriptRoot\.build\local\Nuget"
}

if (!(Get-Command "nuget" -ErrorAction SilentlyContinue))
{
    #install it from somewhere and remove this checked in copy
    add-path "$PSScriptRoot\.build\local\Nuget\"
}



