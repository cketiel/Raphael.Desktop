using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // RE-083: copy of Raphael.Shared/DTOs/VehicleCapabilityDtos.cs. Levels: 0 not configured, 1 AMB, 2 WCH, 3 STR.

    /// <summary>What each vehicle can carry and what each trip needs, in one read.</summary>
    public class VehicleCapabilityDto
    {
        public List<CapacityClassDto> Classes { get; set; } = new();
        public List<SpaceTypeLevelDto> SpaceTypes { get; set; } = new();
        public List<VehicleConfigurationDto> Configurations { get; set; } = new();
        public List<VehicleLevelDto> Vehicles { get; set; } = new();
    }

    public class CapacityClassDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int Level { get; set; }
        public bool TravelsAlone { get; set; }
        public bool CanEdit { get; set; }
    }

    public class SpaceTypeLevelDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? CapacityTypeId { get; set; }
        public string? CapacityType { get; set; }
        public int Level { get; set; }
        /// <summary>RE-093: minutes to board and to leave; 0 means none declared.</summary>
        public float LoadTime { get; set; }
        public float UnloadTime { get; set; }
        public bool CanEdit { get; set; }
    }

    public class VehicleConfigurationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int Level { get; set; }
        public string? Kind { get; set; }
        public int Vehicles { get; set; }
        /// <summary>RE-093: patients on board at once, and how many of them may not be ambulatory.</summary>
        public int MaxOnBoard { get; set; } = 2;
        public int MaxNonAmbulatory { get; set; } = 1;
        public bool CanEdit { get; set; }
        public List<VehicleConfigurationSeatDto> Seats { get; set; } = new();

        /// <summary>For the pill: the kind, or that it carries nothing yet.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string KindText => Helpers.VehicleCapabilityUi.KindText(Level);

        /// <summary>"AMB 2 · WCH 1 (alone)".</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string SeatsText => Helpers.VehicleCapabilityUi.SeatsText(this);
    }

    public class VehicleConfigurationSeatDto
    {
        public int SpaceTypeId { get; set; }
        public string SpaceType { get; set; } = "";
        public string? CapacityType { get; set; }
        public int Level { get; set; }
        public bool TravelsAlone { get; set; }
        public int Quantity { get; set; }
    }

    public class VehicleLevelDto
    {
        public int VehicleId { get; set; }
        public int ConfigurationId { get; set; }
        public int Level { get; set; }
        public string? Kind { get; set; }
    }

    public class CapacityClassUpdateDto
    {
        public int Level { get; set; }
        public bool TravelsAlone { get; set; }
    }

    public class SpaceTypeClassUpdateDto
    {
        public int CapacityTypeId { get; set; }
    }

    public class VehicleConfigurationSaveDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>RE-093: null keeps what the configuration has, as an older Desktop sends.</summary>
        public int? MaxOnBoard { get; set; }
        public int? MaxNonAmbulatory { get; set; }
        public List<VehicleConfigurationSeatSaveDto> Seats { get; set; } = new();
    }

    /// <summary>RE-093, admin: minutes a patient of this space type takes to board and to leave.</summary>
    public class SpaceTypeTimesUpdateDto
    {
        public float LoadTime { get; set; }
        public float UnloadTime { get; set; }
    }

    public class VehicleConfigurationSeatSaveDto
    {
        public int SpaceTypeId { get; set; }
        public int Quantity { get; set; }
    }
}
