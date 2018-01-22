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
        private ILogger<DockerController> _log;

        public DockerController(ILogger<DockerController> log)
        {
            _client = new DockerClientConfiguration(new Uri("http://localhost:2375"))
                                     .CreateClient();
            _log = log;                                     
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
        public async Task<IActionResult> Build([FromBody]BuildRequest req)

        {
            //figure out so
            var auth = new AuthConfig(){
                Username = "paulgmiller",
                Password =  System.Environment.GetEnvironmentVariable("dockerpassword")
            };
            var authdict = new Dictionary<string, AuthConfig> { 
                { "docker.io", auth }
            };
            _log.LogInformation($"fechign manifest for {req.VSTSDropUri}");
            var drop = new VSTSDropProxy(req.VSTSDropUri);
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var imageparams = new ImageBuildParameters();
            imageparams.Tags = new List<string> {req.tag};
            imageparams.AuthConfigs = authdict;
            
            try 
            {
                _log.LogInformation($"Putting {req.VSTSDropUri} in {tempDirectory}");
                await drop.Materialize(tempDirectory);
                using (var tar = CreateTar(tempDirectory))
                {
                    //since apparently we use a tarball for context we don't really need to be in the same pod.
                    _log.LogInformation($"Building t{req.tag}");
                    var image = await _client.Images.BuildImageFromDockerfileAsync(tar, imageparams);                        
                    using (StreamReader reader = new StreamReader( image ))
                    {
                        _log.LogInformation(await reader.ReadToEndAsync());
                    }
                    _log.LogInformation($"Pushing image");
                    await _client.Images.PushImageAsync(req.tag, new ImagePushParameters(), auth, new ProgressDumper(_log));
                }
                _log.LogInformation($"Putting {req.VSTSDropUri} in {tempDirectory}");
                return Ok(tempDirectory);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [HttpPost]
        [Route("BuildTest")]
        public async Task<IActionResult> BuildTest()

        {
            var r = new BuildRequest { 
                VSTSDropUri = "https://msasg.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/MSASG_CloudDeploy/4a5ab0eef87b35799d6878d51d13db7d756b4cb9/3583b457-5fc1-a5cc-8290-dd466e142bab?root=retail/amd64/app/helloworld",
                tag = "docker.io/paulgmiller/blugh5:test"
            };
            return await Build(r);

        }

        //gross   q
        private Stream CreateTar(string directory)
        {
            var filesInDirectory = new DirectoryInfo(directory).GetFiles();
            string tarfile = Path.Combine(directory, Path.GetRandomFileName());
            using (TarArchive tarArchive = TarArchive.CreateOutputTarArchive(System.IO.File.Create(tarfile), TarBuffer.DefaultBlockFactor))
            {
                tarArchive.RootPath = directory; //otherwise files aren't relative.
                foreach (FileInfo fileToBeTarred in filesInDirectory)
                {
                    TarEntry entry = TarEntry.CreateEntryFromFile(fileToBeTarred.FullName);
                    tarArchive.WriteEntry(entry, true);
                }
            }
            return System.IO.File.OpenRead(tarfile);
        }

    }

    public class ProgressDumper : IProgress<JSONMessage>
    {
        private ILogger<DockerController> _log;
        public ProgressDumper(ILogger<DockerController> logger)
        {
            _log = logger;
        }
        public void Report(JSONMessage msg) {
            try 
            {
                _log.LogInformation(JsonConvert.SerializeObject(msg));
            }
            catch (Exception e)
            {}
        }
    }
  

    public class BuildRequest
    {
        public string VSTSDropUri;
        public string tag;
        //docker repo info?        
    }
}
