using Safir.Shared.Models.User_Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IUserService
    {
        // Find user potentially by encoded name (as stored in DB)
        Task<SALA_DTL?> GetUserByEncodedUsernameAsync(string encodedUsername);
        // Or find potentially by decoded name (requires decoding all usernames first)
        Task<SALA_DTL?> GetUserByDecodedUsernameAsync(string decodedUsername);

        Task<bool> CanViewSubordinateTasksAsync();
    }
}
