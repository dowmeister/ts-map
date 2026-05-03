using System;

namespace TsMap.Routing
{
    // Routing-layer vehicle type flags. Application-defined constants for the
    // routing graph — not direct binary field values from the .ppd format.
    //
    // NavCurve.AllowedVehicles is a 2-bit value in the .ppd flags field:
    //   0 (PlayerOnly)    -> Passenger
    //   1 (SmallVehicles) -> Passenger | Truck
    //   2 (LargeVehicles) -> Truck
    //   3 (AllVehicles)   -> Passenger | Truck | Bus
    // Train is reserved for ferry/tunnel connections added in Phase 2.
    [Flags]
    public enum VehicleType : uint
    {
        None      = 0,
        Passenger = 0x1,
        Truck     = 0x2,
        Bus       = 0x4,
        Train     = 0x8,
    }
}
