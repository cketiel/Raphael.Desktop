using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IRoleService
    {
        Task<List<Role>> GetRolesAsync();
        Task<Role> AddRoleAsync(Role role);
        Task<bool> UpdateRoleAsync(int id, Role role);
        Task<bool> DeleteRoleAsync(int id);
    }
}
