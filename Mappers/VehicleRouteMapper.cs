using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Mappers
{
    public static class VehicleRouteMapper
    {
        // Método de extensión para convertir un Modelo a un DTO
        public static VehicleRouteDto ToDto(this VehicleRoute model)
        {
            if (model == null) return null;

            var dto = new VehicleRouteDto
            {
                Name = model.Name,
                Description = model.Description,
                DriverId = model.DriverId,
                VehicleId = model.VehicleId,
                Garage = model.Garage,
                GarageLatitude = model.GarageLatitude,
                GarageLongitude = model.GarageLongitude,
                SmartphoneLogin = model.SmartphoneLogin,
                FromDate = model.FromDate,
                ToDate = model.ToDate,
                FromTime = model.FromTime,
                ToTime = model.ToTime,

                // Mapear las colecciones, manejando el caso de que sean nulas
                Suspensions = model.Suspensions?.Select(s => new RouteSuspensionDto
                {
                    Id = s.Id, // Importante para la actualización
                    SuspensionStart = s.SuspensionStart,
                    SuspensionEnd = s.SuspensionEnd,
                    Reason = s.Reason
                }).ToList(),

                Availabilities = model.Availabilities?.Select(a => new RouteAvailabilityDto
                {
                    DayOfWeek = a.DayOfWeek,
                    StartTime = a.StartTime,
                    EndTime = a.EndTime,
                    IsActive = a.IsActive
                }).ToList(),

                FundingSources = model.FundingSources?.Select(fs => new RouteFundingSourceDto
                {
                    FundingSourceId = fs.FundingSourceId
                }).ToList()
            };

            return dto;
        }
    }
    
}
