using System;
using System.Collections.Generic;
using System.IO; 
using System.Linq; 
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

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

        [HttpPost]
        [Route("Build")]
        public async Task<IActionResult> Build([FromBody]BuildRequest req)

        {
            var drop = new VSTSDropProxy(req.VSTSDropUri);
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try 
            {
                await drop.Materialize(tempDirectory);
                //this is dumb. Can we create the tar from the download streams directly?
                var filesInDirectory = new DirectoryInfo(tempDirectory).GetFiles();
                string tarfile = Path.Combine(tempDirectory, Path.GetRandomFileName());
                using (TarArchive tarArchive = TarArchive.CreateOutputTarArchive(System.IO.File.Create(tarfile), TarBuffer.DefaultBlockFactor))
                {
                    foreach (FileInfo fileToBeTarred in filesInDirectory)
                    {
                        TarEntry entry = TarEntry.CreateEntryFromFile(fileToBeTarred.FullName);
                        tarArchive.WriteEntry(entry, true);
                    }
                }
                
                //since apparently we use a tarball for context we don't really need to be in the same pod.
                var image = await _client.Images.BuildImageFromDockerfileAsync(System.IO.File.OpenRead(tarfile), new ImageBuildParameters());                        
                using (StreamReader reader = new StreamReader( image ))
                    return Ok(reader.ReadToEndAsync());
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

    }

    public class BuildRequest
    {
        public string VSTSDropUri;
        public string tag;
        //docker repo info? 
        
    }
}
