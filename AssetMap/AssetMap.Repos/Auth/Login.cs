using System;
using System.Collections.Generic;
using System.Text;

namespace AssetMap.Repos.Auth
{
    public static class Login
    {
        public static (bool, string) TryLogin(string username, string password)
        {
            bool loged = false;
            string statusCode = "";

#if DEBUG
            if (username == "demo" && password == "demo")
            {
                loged = true;
                statusCode = "OK";
            }
#endif





            return (loged, statusCode);
        }
    }
}
