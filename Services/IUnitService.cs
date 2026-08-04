using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public interface IUnitService
    {
        Task<List<Unit>> GetUnitsAsync();
    }
}
