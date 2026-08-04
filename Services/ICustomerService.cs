using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface ICustomerService
    {
        Task<List<Customer>> GetAllCustomersAsync();
        Task<Customer> GetCustomerByIdAsync(int id);
        Task<Customer> CreateCustomerAsync(CustomerCreateDto customer);
        Task<bool> UpdateCustomerAsync(int id, CustomerCreateDto customer);
        Task<bool> DeleteCustomerAsync(int id);
        Task<Customer> GetCustomerByRiderIdAsync(string riderId);
    }
}
