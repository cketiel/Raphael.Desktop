using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IUserService
    {
        Task<List<User>> GetUsersAsync();
        Task<User> GetUserByIdAsync(int id); 
        Task<User> AddUserAsync(UserCreateDto userDto);
        Task<bool> UpdateUserAsync(UserUpdateDto userDto);
        Task<bool> DeleteUserAsync(int id);
        Task<bool> ChangePasswordAsync(ChangePasswordDto dto);
        /// <summary>An admin sets someone else's password without the current one.</summary>
        Task ResetPasswordAsync(int userId, string newPassword, string confirmPassword);
    }
}
