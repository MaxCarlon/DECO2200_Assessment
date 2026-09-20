using UnityEngine;

namespace Levelstep
{
    public enum RouteType
    {
        Easy = 0,
        Moderate = 1,
        Hard = 2
    }

    public class HoldRouteTag : MonoBehaviour
    {
        [Tooltip("Which difficulty route this hold belongs to. Only the hold matching the currently selected difficulty is fully lit and grabbable.")]
        public RouteType routeType = RouteType.Easy;
    }
}