using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace RSSFeedLoaderService
{
    // sc create "RSSFeedLoaderService" binpath="C:\Users\a694355\Documents\RSSFeedLoaderService\RSSFeedLoaderService\RSSFeedLoaderService.exe --contentRoot C:\Users\a694355\Documents\RSSFeedLoaderService\RSSFeedLoaderService"
    // run this command in cmd as admin

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private readonly IConfiguration _configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LoadRSSFeeds init process");
                LoadRSSFeedsProcess();
                _logger.LogInformation("LoadRSSFeeds end process");
                await Task.Delay(3600000, stoppingToken);
            }
        }

        private void LoadRSSFeedsProcess()
        {
            try
            {
                var instanceDataSections= _configuration.GetSection("InstanceData").GetChildren();

                foreach (var instanceDataSection in instanceDataSections)
                {
                    SaveRSSinDB(instanceDataSection);

                }
                _logger.LogInformation("Load RSSFeeds successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load RSSFeeds error");
            }
        }

        private void SaveRSSinDB(IConfigurationSection instanceDataSection)
        {
            DataSet dsRSS = GetDataset("select distinct rssURL, max(id) ID from sys_config_RSSFeed group by rssURL", instanceDataSection["DBConnectionString"]);
            if (dsRSS.Tables.Count > 0 && dsRSS.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow rw in dsRSS.Tables[0].Rows)
                {
                    SaveInBDByRSS(rw["ID"]?.ToString(), rw["rssURL"]?.ToString(), instanceDataSection["DBConnectionString"], instanceDataSection["InstanceRootUrl"]);
                }
            }
        }

        private void SaveInBDByRSS(string idA, string rssURL, string connString, string instanceRootUrl)
        {
            string id = idA;
            byte[] data;

            try
            {
                using (WebClient webClient = new WebClient())
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    data = webClient.DownloadData(rssURL);
                }

                System.Globalization.CultureInfo ci = new System.Globalization.CultureInfo("en-US");
                ci.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
                ci.DateTimeFormat.LongTimePattern = "HH:mm:ss";
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;

                string str = Encoding.UTF8.GetString(data);

                XDocument xd = XDocument.Parse(str);

                xd = RemoveNamespace(xd);

                var persons = from p in xd.Descendants()
                              where p.Name.LocalName == "item"
                              select p;
                List<XElement> listAux = new List<XElement>();
                listAux = persons.ToList();
                if (listAux.Count > 0)
                {
                    foreach (XElement element in listAux)
                    {
                        try
                        {

                            string title, imgUrl, content, url, pubDate;
                            string subtitle = "";

                            title = element.Element("title").Value.ToString().Replace("'", "''");
                            imgUrl = Regex.Match(element.Element("description").Value, "<img.+?src=[\"'](.+?)[\"'].+?>", RegexOptions.IgnoreCase).Groups[1].Value;
                            content = element.Element("description").Value.ToString().Replace("'", "''");
                            content = Regex.Replace(content, "<.*?>", String.Empty);
                            url = element.Element("link").Value.ToString().Replace("'", "''"); ;
                            if (element.Element("subtitle") != null)
                            {
                                subtitle = element.Element("subtitle").Value.ToString().Replace("'", "''"); ;
                            }
                            pubDate = (element.Element("pubDate") == null) ? element.Element("date").Value : element.Element("pubDate").Value;
                            string sql = "";

                            if (string.IsNullOrEmpty(imgUrl))
                            {
                                imgUrl = instanceRootUrl + "/imgs/randomImage8.jpg";
                            }

                            DataSet dsRSS = GetDataset("select * from RSSFeeds where title = '" + title + "'", connString);
                            if (dsRSS.Tables.Count == 0 || dsRSS.Tables[0].Rows.Count == 0)
                            {
                                sql += "INSERT INTO RSSFeeds ([content],[imgURl],[url],[title],[subtitle],[pubDate],[idRSS]) ";
                                sql += "Values ('" + content + "', '" + imgUrl + "', '" + url + "', '" + title + "' ,'" + subtitle + "', '" + DateTime.Parse(pubDate) + "'," + id + "); ";
                                ExecuteNonQuery(sql, connString);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Load RSSFeeds error");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load RSSFeeds error");
            }
        }

        private static XDocument RemoveNamespace(XDocument xdoc)
        {
            foreach (XElement e in xdoc.Root.DescendantsAndSelf())
            {
                if (e.Name.Namespace != XNamespace.None)
                {
                    e.Name = XNamespace.None.GetName(e.Name.LocalName);
                }
                if (e.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.Namespace != XNamespace.None).Any())
                {
                    e.ReplaceAttributes(e.Attributes().Select(a => a.IsNamespaceDeclaration ? null : a.Name.Namespace != XNamespace.None ? new XAttribute(XNamespace.None.GetName(a.Name.LocalName), a.Value) : a));
                }
            }

            return xdoc;
        }

        private static DataSet GetDataset(string SQLQuery, string connection)
        {

            SqlDataAdapter sqlda = new SqlDataAdapter(SQLQuery, connection);
            DataSet ds = new DataSet("result");
            sqlda.Fill(ds);

            return ds;
        }

        private static int ExecuteNonQuery(string SQLQuery, string connection)
        {
            SqlConnection sqlc = new SqlConnection(connection);
            sqlc.Open();

            SqlCommand sqlcom = new SqlCommand(SQLQuery, sqlc);
            int result = sqlcom.ExecuteNonQuery();

            sqlc.Close();

            return result;
        }
    }
}