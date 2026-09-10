using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace StickyModern {
 public class WeatherCity {
  public string Name; public double Latitude, Longitude;
  public override string ToString() { return Name; }
 }
 static class DayInfo {
  static readonly ChineseLunisolarCalendar lunar = new ChineseLunisolarCalendar();
  static readonly string[] phrases = {"做好今天的工作", "按计划，逐项完成", "保持专注，继续前进", "专注当下，稳步推进", "认真做好每一件事", "安排好工作，也记得休息", "一步一步，把事情做好"};
  public static string Headline(DateTime day) { return phrases[(int)(day.Date - new DateTime(2000,1,1)).TotalDays % phrases.Length]; }
  public static string Lunar(DateTime day) {
   if(day < lunar.MinSupportedDateTime || day > lunar.MaxSupportedDateTime) return "";
   int y = lunar.GetYear(day), m = lunar.GetMonth(day), d = lunar.GetDayOfMonth(day), leap = lunar.GetLeapMonth(y);
   bool isLeap = leap > 0 && m == leap; if(leap > 0 && m >= leap) m--;
   string[] months = {"正","二","三","四","五","六","七","八","九","十","冬","腊"};
   string digits = "一二三四五六七八九十";
   string date = d <= 10 ? "初" + digits[d-1] : d < 20 ? "十" + digits[d-11] : d == 20 ? "二十" : d < 30 ? "廿" + digits[d-21] : "三十";
   return "农历" + (isLeap ? "闰" : "") + months[m-1] + "月" + date;
  }
  public static string WeatherName(int code) {
   if(code == 0) return "晴"; if(code <= 2) return "多云"; if(code == 3) return "阴"; if(code == 45 || code == 48) return "雾";
   if(code >= 51 && code <= 57) return "毛毛雨"; if(code >= 61 && code <= 67) return "雨"; if(code >= 71 && code <= 77) return "雪"; if(code >= 80 && code <= 82) return "阵雨"; if(code == 85 || code == 86) return "阵雪"; if(code >= 95 && code <= 99) return "雷雨"; return "天气未知";
  }
 }
 static class WeatherService {
  static WeatherService() { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; }
  static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
  public static List<WeatherCity> LocalCities(string name) {
   var cities = new[] {
    new WeatherCity { Name = "北京 · 北京市 · 中国", Latitude = 39.9042, Longitude = 116.4074 },
    new WeatherCity { Name = "上海 · 上海市 · 中国", Latitude = 31.2304, Longitude = 121.4737 },
    new WeatherCity { Name = "广州 · 广东省 · 中国", Latitude = 23.1291, Longitude = 113.2644 },
    new WeatherCity { Name = "深圳 · 广东省 · 中国", Latitude = 22.5431, Longitude = 114.0579 },
    new WeatherCity { Name = "杭州 · 浙江省 · 中国", Latitude = 30.2741, Longitude = 120.1551 },
    new WeatherCity { Name = "成都 · 四川省 · 中国", Latitude = 30.5728, Longitude = 104.0668 },
    new WeatherCity { Name = "武汉 · 湖北省 · 中国", Latitude = 30.5928, Longitude = 114.3055 },
    new WeatherCity { Name = "南京 · 江苏省 · 中国", Latitude = 32.0603, Longitude = 118.7969 },
    new WeatherCity { Name = "香港 · 中国", Latitude = 22.3193, Longitude = 114.1694 }
   };
   string query = name.Trim(); var result = new List<WeatherCity>();
   foreach(var city in cities) { string shortName = city.Name.Split('·')[0].Trim(); if(query == shortName || query == shortName + "市") result.Add(city); }
   return result;
  }
  public static string ErrorMessage(Exception e) {
   if(e is System.Threading.Tasks.TaskCanceledException) return "连接超时，请重试";
   for(Exception cause = e; cause != null; cause = cause.InnerException) { var web = cause as System.Net.WebException; if(web != null) { if(web.Status == System.Net.WebExceptionStatus.SecureChannelFailure || web.Status == System.Net.WebExceptionStatus.TrustFailure) return "安全连接失败，请检查系统时间与证书"; if(web.Status == System.Net.WebExceptionStatus.NameResolutionFailure) return "无法解析天气服务器地址"; } }
   return "天气服务连接失败，请稍后重试";
  }
  static async Task<Dictionary<string,object>> Get(string url) {
   string body = await client.GetStringAsync(url); return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(body);
  }
  public static async Task<List<WeatherCity>> Search(string name) {
   var local = LocalCities(name); if(local.Count > 0) return local;
   var data = await Get("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name.Trim()) + "&count=8&language=zh&format=json");
   var result = new List<WeatherCity>(); if(!data.ContainsKey("results")) return result;
   foreach(var obj in (System.Collections.IEnumerable)data["results"]) { var city = (Dictionary<string,object>)obj;
    string label = (string)city["name"] + (city.ContainsKey("admin1") ? " · " + city["admin1"] : "") + (city.ContainsKey("country") ? " · " + city["country"] : "");
    result.Add(new WeatherCity { Name = label, Latitude = Convert.ToDouble(city["latitude"]), Longitude = Convert.ToDouble(city["longitude"]) });
   } return result;
  }
  public static async Task<string> Fetch(WeatherCity city) {
   var data = await Get("https://api.open-meteo.com/v1/forecast?latitude=" + city.Latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" + city.Longitude.ToString(CultureInfo.InvariantCulture) + "&current=temperature_2m,weather_code&timezone=auto&forecast_days=1");
   var current = (Dictionary<string,object>)data["current"];
   return DayInfo.WeatherName(Convert.ToInt32(current["weather_code"])) + "  " + Math.Round(Convert.ToDouble(current["temperature_2m"])).ToString(CultureInfo.InvariantCulture) + "°C";
  }
 }
}
