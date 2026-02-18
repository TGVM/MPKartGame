using UnityEngine;

namespace Kart
{
    public interface IDrive
    {
        bool IsBraking { get; }
        Vector2 Move { get; }

        void Enable();
    }
}