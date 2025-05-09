using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Permissions
{
    public class UserPermissionDto
    {
        public string FormName { get; set; } // TFORMS.FORMNAME
        public int UserCo { get; set; }      // SAL_CHEK.USERCO
        public bool Run { get; set; }        // SAL_CHEK.RUN
        public bool See { get; set; }
        public bool Inp { get; set; }
        public bool Upd { get; set; }
        public bool Del { get; set; }
    }
}
