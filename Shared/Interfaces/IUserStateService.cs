using Safir.Shared.Models.User_Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IUserStateService
    {
        Task<UserStateDto?> GetUserStateAsync(int userId);
        Task SaveUserStateAsync(int userId, UserStateDto state);
        Task ClearUserStateAsync(int userId);
    }
}
