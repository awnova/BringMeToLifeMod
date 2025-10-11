using Newtonsoft.Json;
using Comfort.Common;
using SPT.Common.Http;
using System.Collections.Generic;
using EFT;

namespace RevivalMod.Helpers
{
    internal class Utils
    {
        public static T ServerRoute<T>(string url, object data = default)
        {
            string json = JsonConvert.SerializeObject(data);
            var req = RequestHandler.PostJson(url, json);
            return JsonConvert.DeserializeObject<T>(req);
        }
        public static string ServerRoute(string url, object data = default)
        {
            string json;
            if (data is string v)
            {
                Dictionary<string, string> dataDict = new()
                {
                    { "data", v }
                };
                json = JsonConvert.SerializeObject(dataDict);
            }
            else
            {
                json = JsonConvert.SerializeObject(data);
            }

            return RequestHandler.PutJson(url, json);
        }

        public static Player GetYourPlayer()
        {
            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            if (player == null || !player.IsYourPlayer)
                return null;

            return player;
        }

        public static Player GetPlayerById(string id)
        {
            Player player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(id);
            if (player == null) return null;
            return player;
        }

        public static List<Player> GetAllPlayersAndBots()
        {
            return Singleton<GameWorld>.Instance.AllAlivePlayersList;
        }

    }
}
