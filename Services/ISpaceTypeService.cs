using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface ISpaceTypeService
    {
        Task<List<SpaceType>> GetSpaceTypesAsync();
        Task<SpaceType> CreateSpaceTypeAsync(SpaceType spaceType);

        Task<SpaceType> GetSpaceTypeByNameAsync(string name);
        Task DeleteSpaceTypeAsync(int id);
    }
}
