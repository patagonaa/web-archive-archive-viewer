using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WebArchiveArchiveViewer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!HttpListener.IsSupported)
            {
                Console.WriteLine("HttpListener not supported");
                return;
            }

            var basePath = args[0]; // C:\Temp\wayback\websites
            var uriPrefix = args[1]; // http://+:8080/
            var targetDate = DateTime.Parse(args[2], CultureInfo.InvariantCulture); // 2014-01-01
            var replaceUriConfigured = args.Length > 3 ? args[3] : null; // (optional) http://localhost:8080/

            var listener = new HttpListener();
            listener.Prefixes.Add(uriPrefix);
            listener.Start();

            var regex = new Regex(@"^\/([^\/]+)(?:\/(.*))?$", RegexOptions.Compiled);
            while (true)
            {
                try
                {
                    var context = listener.GetContext();
                    var requestUrl = context.Request.Url;
                    var rawPath = requestUrl.PathAndQuery;

                    var match = regex.Match(rawPath);
                    if (!match.Success)
                    {
                        Console.WriteLine($"{context.Request.HttpMethod} {rawPath} 404");
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        continue;
                    }

                    var host = match.Groups[1].Value;
                    var path = match.Groups[2].Value;

                    string replaceUri;
                    if (replaceUriConfigured == null)
                    {
                        var replaceProto = context.Request.Headers["X-Forwarded-Proto"] ?? requestUrl.Scheme;
                        var replaceHost = context.Request.Headers["X-Forwarded-Host"] ?? context.Request.UserHostName;
                        replaceUri = $"{replaceProto}://{replaceHost}/";
                    }
                    else
                    {
                        replaceUri = replaceUriConfigured;
                    }


                    var filePath = GetFilePath(basePath, host, path, targetDate);

                    if (filePath == null)
                    {
                        Console.WriteLine($"{context.Request.HttpMethod} {path} 404");
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        continue;
                    }

                    Console.WriteLine($"{context.Request.HttpMethod} {path} 200");

                    byte[] fileContent = File.ReadAllBytes(filePath);

                    if (filePath.EndsWith(".html") || filePath.EndsWith(".htm"))
                    {
                        context.Response.AddHeader("Content-Type", "text/html");
                        fileContent = Encoding.UTF8.GetBytes(FixLinks(Encoding.UTF8.GetString(fileContent), replaceUri, host));
                    }

                    context.Response.AddHeader("Cache-Control", "public, max-age=86400");

                    context.Response.ContentLength64 = fileContent.Length;
                    try
                    {
                        using (var stream = context.Response.OutputStream)
                        {
                            stream.Write(fileContent);
                        }

                        context.Response.Close();
                    }
                    catch (HttpListenerException)
                    {
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    throw;
                }
            }
        }

        private static string GetFilePath(string basePath, string host, string path, DateTime targetDate)
        {
            if (string.IsNullOrEmpty(path.Trim('/')))
            {
                path = "index.html";
            }

            var cleanRegex = new Regex(@"(?:www.)?(.+?)(?::\d+)?$");

            var match = cleanRegex.Match(host);
            if (!match.Success)
                throw new Exception();

            var cleanedHost = match.Groups[1].Value.ToLowerInvariant();

            var windowsPortSeperator = (char)61498;
            var windowsQuerySeperator = (char)61503;

            var hostVariants = new[] {
                cleanedHost,
                $"www.{cleanedHost}",
                $"{cleanedHost}{windowsPortSeperator}80",
                $"www.{cleanedHost}{windowsPortSeperator}80",
                $"{cleanedHost}:80",
                $"www.{cleanedHost}:80"
            };

            var directories = new List<string>();
            foreach (var hostVariant in hostVariants)
            {
                var hostDirPath = Path.Combine(basePath, hostVariant);
                if (Directory.Exists(hostDirPath))
                    directories.AddRange(Directory.GetDirectories(hostDirPath));
            }

            var directoriesDesc = directories.OrderByDescending(x => x.Split('/', '\\').Last());

            var closestBeforeTarget = directoriesDesc.Where(x => DateTime.ParseExact(x.Split('/', '\\').Last(), "yyyyMMddHHmmss", null) <= targetDate);
            var closestAfterTarget = directoriesDesc.Where(x => DateTime.ParseExact(x.Split('/', '\\').Last(), "yyyyMMddHHmmss", null) > targetDate).Reverse();
            var searchDirs = closestBeforeTarget.Concat(closestAfterTarget);

            foreach (var scrapeDirectory in searchDirs)
            {
                var filePath = Path.Combine(scrapeDirectory, path);
                if (File.Exists(filePath))
                {
                    return filePath;
                }
                if (filePath.Contains('?'))
                {
                    if (File.Exists(filePath.Replace('?', windowsQuerySeperator)))
                    {
                        return filePath;
                    }

                    var dir = Path.GetDirectoryName(filePath);
                    var file = Path.GetFileName(filePath);

                    if (Directory.Exists(dir))
                    {
                        var foundFiles = Directory.GetFiles(dir, file.Split('?')[0] + "*", SearchOption.TopDirectoryOnly);
                        if (foundFiles.Length > 0)
                            return foundFiles[0];
                    }
                }
            }

            return null;
        }

        private static string FixLinks(string html, string uriPrefix, string host)
        {
            html = Regex.Replace(html, @"(https?:\/\/)", uriPrefix);
            html = Regex.Replace(html, @"(=[""'])(\/[^\/])", $"$1{uriPrefix + host}$2");
            return html;
        }
    }
}
