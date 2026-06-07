using System;
using System.Collections.Generic;
using System.Text;

namespace AssetMap.Repos.Auth
{
    public static class Login
    {
        public static (bool, string) TryLogin(string username, string password)
        {
#if DEBUG
            if (username == "demo" && password == "demo")
                return (true, "Succefuly logged in");
#endif

            if (username.IsWhiteSpace())
                return (false, "Username is empty");

            if (password.IsWhiteSpace()) 
                return (false, "Password is empty");

            // TODO: call API a přepsat result na true, zatim je false aby se jen tak nepřihlašovalo


            return (false, "Nah Bro");
        }
    }
}
