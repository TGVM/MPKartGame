using Unity.Netcode.Components;
using UnityEngine;

namespace Kart {
    
    
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform {

        public enum AuthorityMode
        {
            Server,
            Client
        }

        public AuthorityMode authorityMode = AuthorityMode.Client;

        protected override bool OnIsServerAuthoritative() => authorityMode == AuthorityMode.Server;
    }
}