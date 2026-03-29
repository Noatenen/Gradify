using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthWithAdmin.Shared.AuthSharedModels
{
    public class AdminResults
    {
        public string Result { get; set; }
        public UserForAdmin User { get; set; }
    }
}
