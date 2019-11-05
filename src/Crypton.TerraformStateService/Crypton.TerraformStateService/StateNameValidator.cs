using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Crypton.TerraformStateService
{
    public class StateNameValidator : RegularExpressionAttribute
    {
        public StateNameValidator() : base("[a-z0-9-_]{4,100}")
        {
        }

    }
}
