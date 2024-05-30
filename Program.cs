using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using HtmlAgilityPack;

namespace GooglePlayStoreCrawler
{
    internal class AggregateRating
    {
        public string ratingValue { get; set; }
    }

    internal class LdJsonData
    {
        public string name { get; set; }
        public string applicationCategory { get; set; }
        public AggregateRating aggregateRating { get; set; }
    }

    internal class AppData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float Stars { get; set; }
        public string Category { get; set; }
        public string CurrentVersion { get; set; }
    }

    class Program
    {
        private const string FetchError = "FETCH_ERROR";

        private const string PlayHostname = "https://play.google.com";
        private const string HomepageUrl = "https://play.google.com/store/apps";
        private const string SitemapsUrl = "https://play.google.com/sitemaps/sitemaps-index-0.xml";
        private const string AppUrlSubstring = "/store/apps/details";
        private const int AppsAmount = 1040;
        private const int ThreadsAmount = 13;

        private static readonly HttpClient httpClient = new HttpClient();

        private static readonly HtmlWeb htmlClient = new HtmlWeb
        {
            UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        };

        private static async Task Main(string[] args)
        {
            var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var currentDir = Directory.GetCurrentDirectory();
            var jsonDir = Directory.CreateDirectory($"{currentDir}/apps_metadata");

            var sitemapsXml = await httpClient.GetStringAsync(SitemapsUrl);
            var sitemapsDoc = new XmlDocument();
            sitemapsDoc.LoadXml(sitemapsXml);

            var tasks = Enumerable.Range(0, ThreadsAmount)
                .Select(i => CrawlPlayStore(AppsAmount / ThreadsAmount, i, sitemapsDoc, jsonDir.FullName));
            var taskRes = await Task.WhenAll(tasks);
            var appData = taskRes.SelectMany(i => i).ToHashSet();

            var timeAfterData = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeSpentForAll = timeAfterData - startTime;

            Console.WriteLine($"Fetched {appData.Count()} apps in {timeSpentForAll}ms");

            Console.WriteLine($"Apps metadata stored at {jsonDir.FullName}");
        }

        private static async Task<List<AppData>> CrawlPlayStore(int urlsCount, int threadIndex, XmlDocument sitemapsDoc,
            string jsonDirPath)
        {
            Console.WriteLine($"Thread {threadIndex} started");

            var appUrls = await RetrieveAppUrlsFromSitemaps(urlsCount, threadIndex, sitemapsDoc);

            Console.WriteLine($"Thread {threadIndex} retrieved {appUrls.Count} URLs");

            var appData = await FetchAppDataParallel(appUrls.ToList(), jsonDirPath);

            Console.WriteLine($"Thread {threadIndex} retrieved {appData.Count} pages");

            return appData;
        }


        private static async Task<HashSet<string>> RetrieveAppUrlsFromSitemaps(int urlsCount,
            int threadIndex, XmlDocument sitemapsDoc)
        {
            var appUrls = new HashSet<string>();
            var sitemapNodes = sitemapsDoc.ChildNodes[1];
            var sitemapIndex = threadIndex * 10;
            while (appUrls.Count < urlsCount && sitemapIndex < sitemapNodes.ChildNodes.Count)
            {
                var sitemapUrl = sitemapNodes.ChildNodes[sitemapIndex].ChildNodes[0].InnerText.Trim();
                var newUrls = await RetrieveAppFromSitemap(sitemapUrl, urlsCount - appUrls.Count);
                appUrls.UnionWith(newUrls);

                sitemapIndex++;
            }

            return appUrls;
        }

        private static async Task<HashSet<string>> RetrieveAppFromSitemap(string sitemapUrl, int amountToFetch)
        {
            var appUrls = new HashSet<string>();

            var sitemapRes = await httpClient.GetStreamAsync(sitemapUrl);
            var decompressor = new GZipStream(sitemapRes, CompressionMode.Decompress);
            var sr = new StreamReader(decompressor);
            var sitemapXml = await sr.ReadToEndAsync();
            var sitemapDoc = new XmlDocument();
            sitemapDoc.LoadXml(sitemapXml);
            foreach (XmlNode childNode in sitemapDoc.ChildNodes[1].ChildNodes)
            {
                var url = childNode.InnerText.Trim();
                if (!url.Contains(AppUrlSubstring)) continue;
                appUrls.Add(url);
                if (appUrls.Count() >= amountToFetch) break;
            }

            return appUrls;
        }


        private static async Task<List<AppData>> FetchAppDataParallel(List<string> urls, string jsonDirPath)
        {
            // this was an attempt to check if it could be faster when batching requests but it in the end it was not 

            // var appData = new List<AppData>();
            // const int batchSize = 20;
            // var numberOfBatches = (int)Math.Ceiling((double)urls.Count() / batchSize);
            //
            // for (var i = 0; i < numberOfBatches; i++)
            // {
            //     var currentIds = urls.Skip(i * batchSize).Take(batchSize);
            //     var tasks = currentIds.Select(GetAppData);
            //     appData.AddRange(await Task.WhenAll(tasks));
            // }

            var tasks = urls.Select(url => GetAndStoreAppData(url, jsonDirPath));
            var appData = await Task.WhenAll(tasks);

            return appData.Where(res => res.Name != FetchError).ToList();
        }

        private static async Task<AppData> GetAndStoreAppData(string url, string jsonDirPath)
        {
            try
            {
                var doc = await htmlClient.LoadFromWebAsync(url);
                var data = ExtractAppData(doc, url);

                await using var createStream = File.Create($"{jsonDirPath}/{data.Id}.json");
                await JsonSerializer.SerializeAsync(createStream, data,
                    new JsonSerializerOptions { WriteIndented = true });

                return data;
            }
            catch (Exception ex)
            {
                return new AppData { Name = FetchError, Category = "N/A", CurrentVersion = "N/A", Stars = -1 };
            }
        }

        private static AppData ExtractAppData(HtmlDocument doc, string url)
        {
            var data = new AppData
            {
                Id = url.Split("id=")[1]
            };

            var ldJsonNode = doc.DocumentNode.SelectSingleNode("//script[contains(@type, 'application/ld+json')]");
            var ldJsonText = ldJsonNode.InnerText.Trim();
            var ldJson = JsonSerializer.Deserialize<LdJsonData>(ldJsonText);

            data.Name = ldJson.name;
            data.Category = ldJson.applicationCategory;
            data.Stars = float.Parse(ldJson.aggregateRating?.ratingValue ?? "0");

            var scriptNodes = doc.DocumentNode.SelectNodes("//script");
            var versionRegex = new Regex(@"(\d+\.)?(\d+\.)(\d+)");

            foreach (var scriptNode in scriptNodes)
            {
                try
                {
                    var scriptText = scriptNode.InnerText;
                    if (!scriptText.Contains($"[\"{data.Name}\"]")) continue;
                    var match = versionRegex.Match(scriptText).Value;
                    data.CurrentVersion = match == "" ? "N/A" : match;
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            return data;
        }

        // this was a first attempt at retrieving URLs starting from the homepage but
        // sitemaps are more reliable, thus this is not used
        private static async Task<HashSet<string>> RetrieveAppUrlsFromHomepage(int urlsCount)
        {
            var urlsToReturn = new HashSet<string>();
            var urlsVisited = new HashSet<string>();
            var urlsToVisit = new Queue<string>();
            urlsToVisit.Enqueue(HomepageUrl);

            while (urlsToReturn.Count < urlsCount)
            {
                try
                {
                    var url = urlsToVisit.Dequeue();
                    urlsVisited.Add(url);

                    Console.WriteLine($"URL: {url}");

                    var doc = await htmlClient.LoadFromWebAsync(url);

                    var aNodes = doc.DocumentNode.SelectNodes("//a");
                    foreach (var aNode in aNodes)
                    {
                        try
                        {
                            var href = aNode.Attributes["href"];

                            if (urlsVisited.Contains(href.Value) ||
                                urlsToVisit.Contains(href.Value)) continue;
                            var completeUrl = PlayHostname + href.Value;
                            urlsToVisit.Enqueue(completeUrl);

                            if (!href.Value.Contains(AppUrlSubstring)) continue;
                            urlsToReturn.Add(completeUrl);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            return urlsToReturn;
        }
    }
}