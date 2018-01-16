using System;
using System.Collections.Generic;
using System.IO; 
using System.Linq; 
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

namespace vstsdockerbuild.Controllers
{
    [Route("docker")]
    public class DockerController : Controller
    {

        private DockerClient _client;

        public DockerController()
        {
            _client = new DockerClientConfiguration(new Uri("http://localhost:2375"))
                                     .CreateClient();
        }
        
        // GET api/values
        [HttpGet]
        [Route("version")]
        public async Task<IActionResult> Version()
        {
            var version = await _client.System.GetVersionAsync();
            return Ok(version.Version);
        }

        [HttpGet]
        [Route("Images")]
        public async Task<IActionResult> Images()
        {
            var images = await _client.Images.ListImagesAsync(new ImagesListParameters
            {
                All = true
            });
            return Ok(JsonConvert.SerializeObject(images));
        }

        /*
            >more .\foo.json
{
  VSTSDropUri : "https://msasg.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/MSASG_CloudDeploy/4a5ab0eef87b35799d6878d51d13db7d756b4cb9/3583b457-5
fc1-a5cc-8290-dd466e142bab?root=retail/amd64/app/helloworld",
  tag :   "docker.io/paulgmiller/blugh:test"

  >irm http://127.0.0.1:5000/docker/Build -body $json -contenttype "applica
tion/json" -method post
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
}
         */
        [HttpPost]
        [Route("Build")]
        public async Task<IActionResult> Build([FromBody]BuildRequest req, 
                                               [FromServices]ILogger<DockerController> log)

        {
            //figure out so
            var auth = new AuthConfig(){
                Username = "paulgmiller",
                Password =  System.Environment.GetEnvironmentVariable("dockerpassword")
            };
            var authdict = new Dictionary<string, AuthConfig> { 
                { "docker.io", auth }
            };

            var drop = new VSTSDropProxy(req.VSTSDropUri);
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            
            try 
            {
                await drop.Materialize(tempDirectory);
                using (var tar = CreateTar(tempDirectory))
                {
                    //since apparently we use a tarball for context we don't really need to be in the same pod.
                    var image = await _client.Images.BuildImageFromDockerfileAsync(tar, new ImageBuildParameters(){ 
                        
                        Tags = { req.tag },
                        AuthConfigs = authdict

                    });                        
                    using (StreamReader reader = new StreamReader( image ))
                    {
                        log.LogInformation(await reader.ReadToEndAsync());
                    }
                    await _client.Images.PushImageAsync(req.tag, new ImagePushParameters(), auth, new ProgressDumper());
                }
                
                return Ok();
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        //gross 
        private Stream CreateTar(string directory)
        {
            var filesInDirectory = new DirectoryInfo(directory).GetFiles();
            string tarfile = Path.Combine(directory, Path.GetRandomFileName());
            using (TarArchive tarArchive = TarArchive.CreateOutputTarArchive(System.IO.File.Create(tarfile), TarBuffer.DefaultBlockFactor))
            {
                foreach (FileInfo fileToBeTarred in filesInDirectory)
                {
                    TarEntry entry = TarEntry.CreateEntryFromFile(fileToBeTarred.FullName);
                    tarArchive.WriteEntry(entry, true);
                }
            }
            return System.IO.File.Create(tarfile);
        }

    }

    public class ProgressDumper : IProgress<JSONMessage>
    {
        public void Report(JSONMessage msg) {}
    }
  

    public class BuildRequest
    {
        public string VSTSDropUri;
        public string tag;
        //docker repo info?        
    }
}
