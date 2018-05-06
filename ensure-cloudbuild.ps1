#Cloudbuild needs to get an installer. Until then the only way to get it is to grab it from the corext package and call it's init script.
#You still might want cloudbuild for local caching and unittest running.

function add-path([string] $p) {
    if (! $env:Path.EndsWith(';')) {$env:Path += ';' }
    $env:PATH += "$p;"
}


# Originally pulled from:
# https://social.technet.microsoft.com/Forums/scriptcenter/en-US/9bc0a9c6-50a1-41d2-806d-5a0747d9d1dd/setting-environment-variables-with-a-batch-file?forum=ITCG
# Only used for quickbuild's init.cmd which needs to propogate back variable
function Init-Cmdline {
    param([String] $ScriptName)
    [string] $startTag = "ENVIRONMENT_VARIABLES_START"
    [string] $tempFile = [IO.Path]::GetTempFileName()
    & "$ENV:Comspec" /c " ""$ScriptName"" $ARGS && echo $startTag > $tempFile && set >> $tempFile"
    $a = Get-Content $tempFile
    $foundStart = $false
    $a.Split("`n") | foreach-object {
        $line = $_.Trim()
        if ((-not $foundStart) -and ($line -eq $startTag)) {
            $foundStart = $true
        }
        if ($foundStart -and $_ -match "^(.*?)=(.*)$") {
            set-item ENV:$($MATCHES[1]) $MATCHES[2]
        }
    }

    Remove-Item $tempFile
}

& "$PSScriptRoot\ensure-msbuild.ps1"

#cloudbuild wants admin
$isadmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole( [Security.Principal.WindowsBuiltInRole] "Administrator")
if (!$isadmin) 
{
    throw "You are not running as an administrator.  Quickbuild.exe requires admin rights for symlinking."
}

#should we do get-command for quickbuild? How would it be in path?

$packagesroot = "$PSScriptRoot\.build\Local\packages"
nuget install CloudBuild.OnCorext -OutputDirectory $packagesroot -Source https://cloudbuild.pkgs.visualstudio.com/_packaging/CloudBuild/nuget/v3/index.json
#check for error

#this is a hacky way to do this.
$cloudbuildpackage =  $(ls "$packagesroot\CloudBuild.OnCorext*")[-1].FullName
if (!$cloudbuildpackage)
{
    throw "Couldn't find $packagesroot\CloudBuild.OnCorext*";
}

#this is the most environment poluting part. Everthing is else is just adding tools to path if they don't exist
Init-Cmdline "$cloudbuildpackage\init.cmd"
#alternatively we add this to the path too
add-path "$cloudbuildpackage\tools\"  


