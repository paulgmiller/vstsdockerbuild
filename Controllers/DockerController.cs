using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Docker.DotNet;

namespace vstsdockerbuild.Controllers
{
    [Route("docker")]
    public class DockerController : Controller
    {
        // GET api/values
        [HttpGet]
        [Route("version")]
        public async Task<IActionResult> Version()
        {
            var client = new DockerClientConfiguration(new Uri("http://localhost:2375"))
                                     .CreateClient();
            var version = await client.System.GetVersionAsync();
            return Ok(version.Version);
        }

       
    }
}
