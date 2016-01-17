using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ClientApi
{
    public class ClientApi
    {
        private readonly string address;
        private readonly string keyid;
        private readonly string secret;
        private int counter;
        private readonly JsonSerializerSettings serializerSettings;
        private readonly Encoding encoding = Encoding.UTF8;
        private readonly SHA256 sha256 = SHA256.Create();

        public ClientApi(string address, string keyid = null, string secret = null)
        {
            this.secret = secret;
            this.keyid = keyid;
            this.counter = (int) (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            this.address = address.TrimEnd('/') + '/';

            serializerSettings = new JsonSerializerSettings
            {
                Converters =
                    new JsonConverter[]
                    {
                        new StringEnumConverter(),
                        new UnixDateTimeConverter(), 
                        new DecimalConverter()
                    }
            };
        }

        private T SendRequest<T>(string command, object parametersObject)
        {
            var parameters = parametersObject.GetType()
                                             .GetProperties()
                                             .Select(
                                                 pi =>
                                                 new KeyValuePair<string, string>(pi.Name,
                                                                                  (string)
                                                                                  Convert.ChangeType(
                                                                                      pi.GetValue(parametersObject, null) ??
                                                                                      "", typeof (string))))
                                             .Where(p => !string.IsNullOrEmpty(p.Value));

            var request = new StringBuilder(address);
            request.Append(command);

            var hasParams = false;

            foreach (var kv in parameters)
            {
                request.Append(hasParams ? '&' : '?');
                hasParams = true;

                request.AppendFormat("{0}={1}", kv.Key, kv.Value);
            }

            var webClient = new WebClient();
            var response = webClient.DownloadString(request.ToString());

            return JsonConvert.DeserializeObject<T>(response, serializerSettings);
        }

        private T SendRequestSecure<T>(string command, object parametersObject)
        {
            var numberParameter = new KeyValuePair<string, string>("number",
                (counter++).ToString(CultureInfo.InvariantCulture));
            var keyIdParameter = new KeyValuePair<string, string>("keyid", keyid);

            var parameters = parametersObject.GetType()
                .GetProperties()
                .Select(
                    pi =>
                        new KeyValuePair<string, string>(pi.Name,
                            (string)
                                Convert.ChangeType(
                                    pi.GetValue(parametersObject, null) ??
                                    "", typeof (string))))
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Concat(new[] {numberParameter, keyIdParameter});

            var requestData = string.Join("&", parameters.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));

            using (var client = new WebClient())
            {
                client.Encoding = encoding;
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                var sign = BitConverter.ToString(sha256.ComputeHash(encoding.GetBytes(requestData + secret)))
                    .Replace("-", "")
                    .ToUpper();
                client.Headers["sign"] = sign;
                var response = client.UploadString(address + command, "POST", requestData);
                return JsonConvert.DeserializeObject<T>(response, serializerSettings);
            }
        }

        public IEnumerable<Symbol> Symbols
        {
            get { return SendRequest<Symbol[]>("symbols", new {}); }
        }

        public IEnumerable<Trade> Trades(string symbol, int count = 1000)
        {
            return SendRequest<Trade[]>("trades", new {symbol, count});
        }

        public IEnumerable<Candle> Candles(string symbol, int timeframe = 60, int count = 1000)
        {
            return SendRequest<Candle[]>("candles", new {symbol, timeframe, count});
        }

        public Depth Depth(string symbol, int depth = 5)
        {
            return SendRequest<Depth>("depth", new {symbol, depth});
        }

        public Balance Balance
        {
            get { return SendRequestSecure<Balance>("balance", new { }); }
        }

        public Order GetOrder(int id)
        {
            return SendRequestSecure<Order>("getorder", new {id});
        }

        public IEnumerable<Order> ActiveOrders(string symbol)
        {
            return SendRequestSecure<Order[]>("myorders", new {symbol});
        }

        public IEnumerable<MyTrade> MyTrades(int count, string symbol = null)
        {
            return SendRequestSecure<MyTrade[]>("mytrades", new { count, symbol });
        }

        public Order AddOrder(string symbol, decimal price, decimal volume, OrderType direction, string comment = null)
        {
            return SendRequestSecure<Order>("addorder", new { symbol, price, volume, direction, comment });
        }

        public Order CancelOrder(long id)
        {
            return SendRequestSecure<Order>("cancelorder", new { id });
        }
    }

    public class Trade
    {
        public long Id { get; set; }
        public string Ticker { get; set; }
        public DateTime Time { get; set; }
        public OrderType OrderType { get; set; }
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
    }

    public enum OrderType
    {
        Buy = 1,
        Sell = -1
    }

    public class Symbol
    {
        public string Name { get; set; }
        public string Currency { get; set; }
        public decimal PriceStep { get; set; }
    }

    public class Depth
    {
        public string Symbol { get; set; }
        public IList<DepthLevel> Bids { get; set; }
        public IList<DepthLevel> Asks { get; set; }
    }

    public class DepthLevel
    {
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
        public OrderType OrderType { get; set; }
    }

    public class Account
    {
        public string Currency { get; set; }
        public decimal Amount { get; set; }
        public decimal Reserved { get; set; }
    }

    public class Balance
    {
        public Balance()
        {
            Accounts = new List<Account>();
        }
        public List<Account> Accounts { get; set; }
    }

    public class MyTrade : Trade
    {
        public long OrderId { get; set; }
    }

    public class Order
    {
        public long Id { get; set; }
        public string Symbol { get; set; }
        public DateTime AddTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
        public decimal InitialVolume { get; set; }
        public OrderType Direction { get; set; }
        public OrderStatus Status { get; set; }
        public string Comment { get; set; }
    }

    public enum OrderStatus
    {
        Unknown = 0,
        Active = 1,
        Done = 2,
        Canceled = 3,
        CrossDealReject = -1,
        NoMoneyReject = -2,
        PaceReject = -3,
        NotFoundReject = -4,
        InvalidPriceReject = -5
    }

    public class Candle
    {
        public string Symbol { get; set; }
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    class DecimalConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((decimal)value).ToString("0.########", CultureInfo.InvariantCulture));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return Convert.ChangeType(reader.Value, typeof(decimal), CultureInfo.InvariantCulture);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(decimal);
        }
    }

    public class UnixDateTimeConverter : DateTimeConverterBase
    {
        private static readonly DateTime UnixStartTime = new DateTime(1970, 1, 1);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue((int)((DateTime)value - UnixStartTime).TotalSeconds);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return UnixStartTime.AddSeconds((int)Convert.ChangeType(reader.Value, typeof(int)));
        }
    }

}
