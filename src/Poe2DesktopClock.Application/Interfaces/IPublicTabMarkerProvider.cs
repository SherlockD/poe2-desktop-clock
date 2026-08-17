using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IPublicTabMarkerProvider
{
    IReadOnlyList<PublicTabMarker> GetMarkers();
}
