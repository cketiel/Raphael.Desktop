using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models
{
    public class MobilityType
    {
        public string SpaceType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public static List<MobilityType> AllMobilityTypes()
        {
            List<MobilityType> ListMobilityTypes = new List<MobilityType>
            {
                new MobilityType {
                    SpaceType = "AMB",
                    Description = "Ambulatory"
                },
                new MobilityType {
                    SpaceType = "BSTR",
                    Description = "BARIATRIC STRETCHER"
                },
                new MobilityType {
                    SpaceType = "BWCH",
                    Description = "WBARIATRIC"
                },
                new MobilityType { // CapacityType = AMB
                    SpaceType = "C2C",
                    Description = "Court to Court"
                },
                new MobilityType {
                    SpaceType = "D2D", // CapacityType = AMB
                    Description = "Door to Door"
                },
                new MobilityType {
                    SpaceType = "STR",
                    Description = "Stretcher"
                },
                new MobilityType {
                    SpaceType = "WCH",
                    Description = "WheelChair"
                },
                new MobilityType {
                    SpaceType = "WCX",
                    Description = "WheelChairX"
                }
            };
            return ListMobilityTypes;          
        }
    }
}
