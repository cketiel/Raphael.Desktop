using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // Copies of Raphael.Shared/DTOs/Catalog/OfficeRelationsDtos.cs (2026-10-11). Who works with whom:
    // a clinic's contracted Providers and its catalog row, and a Provider's clinics
    // (PROVIDER_ACCESS_MODEL.md §3.6).

    public class IntegratorRelationsDto
    {
        public int IntegratorId { get; set; }
        public int? CatalogIntegratorId { get; set; }
        public string? CatalogName { get; set; }
        public List<IntegratorProviderRowDto> Providers { get; set; } = new();
    }

    public class IntegratorProviderRowDto
    {
        public int CatalogProviderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? AccountId { get; set; }
        public string? WorkMode { get; set; }
        public int ActiveUsers { get; set; }
        public DateTime ContractedAtUtc { get; set; }
    }

    public class ProviderClinicRowDto
    {
        public int IntegratorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime ContractedAtUtc { get; set; }
    }
}
