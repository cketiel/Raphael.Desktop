using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // RE-065. Manual copies of Raphael.Shared/DTOs/Compliance/ComplianceDtos.cs, the source of truth:
    // the types the office reads and sends. MyComplianceDto is the Driver's and is not copied.

    public class ComplianceDocumentTypeDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool ForVehicle { get; set; }
        public bool ForDriver { get; set; }
        public bool ForDispatcher { get; set; }
        public List<string> Slots { get; set; } = new();
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
    }

    public class ComplianceDocumentDto
    {
        public int Id { get; set; }
        public int DocumentTypeId { get; set; }
        public string DocumentTypeCode { get; set; } = string.Empty;
        public string DocumentTypeName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        /// <summary>The last valid day, a date with no time.</summary>
        public DateTime ExpiresOn { get; set; }
        public string Status { get; set; } = string.Empty;
        /// <summary>Negative once expired.</summary>
        public int DaysToExpiry { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public string UploadedByName { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string ReviewedByName { get; set; }
        public string RejectionReason { get; set; }
        public List<ComplianceDocumentFileDto> Files { get; set; } = new();
    }

    public class ComplianceDocumentFileDto
    {
        public int Id { get; set; }
        public string Slot { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }

    public class ComplianceItemDto
    {
        public ComplianceDocumentTypeDto Type { get; set; } = new();
        public ComplianceDocumentDto Current { get; set; }
        public ComplianceDocumentDto Latest { get; set; }
    }

    public class ComplianceSubjectDto
    {
        public string Subject { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public bool CanUpload { get; set; }
        public List<ComplianceItemDto> Items { get; set; } = new();
    }

    public class ComplianceDocumentCreateDto
    {
        public int DocumentTypeId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public DateTime ExpiresOn { get; set; }
    }

    public class ComplianceRejectDto
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class ComplianceSummaryDto
    {
        public string Subject { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        /// <summary>People only: DRIVER or DISPATCHER.</summary>
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public int Total { get; set; }
        public int Valid { get; set; }
        public int Expiring { get; set; }
        public int Expired { get; set; }
        public int Pending { get; set; }
        public int Rejected { get; set; }
        public int Missing { get; set; }
        public DateTime? NextExpiry { get; set; }
    }

    public class ComplianceNoticeDto
    {
        public List<int> DriverIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public class ComplianceNoticeResultDto
    {
        public int Sent { get; set; }
    }
}
