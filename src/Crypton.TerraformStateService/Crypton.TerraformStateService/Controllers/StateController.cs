using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Crypton.TerraformStateService.Controllers
{
    [Route("state")]
    [ApiController]
    public class StateController : ControllerBase
    {

        readonly StateDatabase database;

        public StateController(StateDatabase stateDatabase)
        {
            this.database = stateDatabase;
        }


        [HttpGet]
        [Route("{name}")]
        public IActionResult Get([Required, StateNameValidator] string name)
        {
            var state = database.GetState(name);
            if (state != null)
            {
                return Content(state, "application/json");
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        [Route("{name}")]
        public async Task<IActionResult> Update([Required, StateNameValidator] string name, string ID = null) // ID = lock ID
        {
            if (Request.ContentLength == 0 || Request.ContentLength == null)
                return BadRequest();

            string state;
            using (var sr = new StreamReader(Request.Body))
            {
                state = await sr.ReadToEndAsync();
            }

            try
            {
                database.TransactionalUpdate(name, state, ID);
                return Ok();
            }
            catch (StateLockedException ex)
            {
                Response.StatusCode = 423;
                return Content(ex.LockData, "application/json");
            }
        }

        [HttpDelete]
        [Route("{name}")]
        public IActionResult Purge([Required, StateNameValidator] string name, string force = null)
        {
            try
            {
                database.DeleteState(name, !string.IsNullOrEmpty(force));
                return Ok();
            }
            catch (StateLockedException ex)
            {
                Response.StatusCode = 423;
                return null;
            }
        }

        [AcceptVerbs("LOCK")]
        [Route("{name}")]
        public IActionResult Lock([Required, StateNameValidator] string name, [FromBody, Required] LockRequest lockRequest)
        {
            try
            {
                database.TransactionalLock(name, lockRequest);
                return Ok();
            }
            catch (StateLockedException ex)
            {
                Response.StatusCode = 409;
                return Content(ex.LockData, "application/json");
            }
        }

        [AcceptVerbs("UNLOCK")]
        [Route("{name}")]
        public IActionResult Unlock([Required, StateNameValidator] string name, [FromBody, Required] LockRequest lockRequest)
        {
            try
            {
                database.TransactionalUnlock(name, lockRequest);
                return Ok();
            }
            catch (StateLockedException ex)
            {
                Response.StatusCode = 423;
                return Content(ex.LockData, "application/json");
            }
            catch (InvalidOperationException ex) when (ex.Message == "State is not locked")
            {
                Response.StatusCode = 409;
                return null;
            }
        }
    }
}