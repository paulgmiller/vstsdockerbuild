using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace vstsdockerbuild.Controllers
{
    [Route("probes")]
    public class KeepaliveController : Controller
    {
        // GET api/values
        [HttpGet]
        [Route("alive")]
        public IActionResult Alive()
        {
            return Ok("ALIVE");
        }

        [HttpGet]
        [Route("ready")]
        public IActionResult Ready()
        {
            return Ok("READY");
        }
    }
}
