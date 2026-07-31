using System.Collections.Generic;
using System.Linq;

namespace Safir.Shared.Models.Permissions
{
    public class Pay2FormPermDto
    {
        public string FormName { get; set; } = "";
        public string Caption  { get; set; } = "";
        public bool Run { get; set; }
        public bool See { get; set; }
        public bool Inp { get; set; }
        public bool Upd { get; set; }
        public bool Del { get; set; }
    }

    public class Pay2AccessDto
    {
        public int  UserCo { get; set; }
        public bool AclEnforced { get; set; }
        public bool WsScopeEnforced { get; set; }
        public List<Pay2FormPermDto> Forms { get; set; } = new();
        public List<int> AllowedWorkshopIds { get; set; } = new();

        public bool Has(string form, int permVal)
        {
            if (!AclEnforced) return true;
            var f = Forms.FirstOrDefault(x => x.FormName == form);
            if (f == null) return false;

            bool ok = true;
            if ((permVal & 1) != 0) ok &= f.Run;
            if ((permVal & 2) != 0) ok &= f.See;
            if ((permVal & 4) != 0) ok &= f.Inp;
            if ((permVal & 8) != 0) ok &= f.Upd;
            if ((permVal & 16) != 0) ok &= f.Del;
            return ok;
        }

        public bool CanRun(string form) => Has(form, 1); // 1 is Run
    }
}
