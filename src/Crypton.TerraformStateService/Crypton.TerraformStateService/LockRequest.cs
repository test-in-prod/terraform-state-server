using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Crypton.TerraformStateService
{
    public class LockRequest
    {
        [Required, MaxLength(50)]
        public string ID { get; set; }
        public string Operation { get; set; }
        public string Info { get; set; }
        public string Who { get; set; }
        public string Version { get; set; }
        public string Created { get; set; }

        public string Path { get; set; }

    }
}
