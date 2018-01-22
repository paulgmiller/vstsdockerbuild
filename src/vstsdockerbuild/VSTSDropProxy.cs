using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;


namespace vstsdockerbuild
{

    //Let us mock out drop http calls.
    public interface IDropApi
    {
         Task<IEnumerable<VstsFile>> GetVstsManifest(Uri manifestUri, string blobapiversion, string relativeroot);
         Task<Stream> GetContent(string sasurl);
    }

    //Http calls to drop api and azure storage uses the following apis.
    //https://www.1eswiki.com/wiki/VSTS_Drop#Drop_REST_API
    //https://opsstaging.www.visualstudio.com/en-us/docs/integrate/api/drop/manifest?branch=teams%2Fartifact%2Fversion2
    //https://opsstaging.www.visualstudio.com/en-us/docs/integrate/api/blobstore/blobs?branch=teams%2Fartifact%2Fversion2
  
    public class RestfulDropApi : IDropApi
    {
        public static readonly string APIVersionParam = "api-version";

        private string GetVSTSPAT()
        {
            return System.Environment.GetEnvironmentVariable("VSTSPAT") ?? 
                    throw new Exception("Set the vsts PAT");
            
        }

        /// <summary>
        /// Gets the manifest details
        /// </summary>
        /// <param name="manifestUri">The manifest uri.</param>
        /// <param name="vstsToken">The personal access token.</param>
        /// <returns>The manifest details.</returns>         
        public async Task<IEnumerable<VstsFile>> GetVstsManifest(Uri manifestUri, string blobapiversion, 
                                                           string relativeroot)
        {
            //dotnet core  doesn't handle vsts redirects well. Poking both teams about it
            var noredirect = new HttpClientHandler() { AllowAutoRedirect = false };
            using (var client = new HttpClient(noredirect))
            {
                var base64EncodedString = Convert.ToBase64String(Encoding.UTF8.GetBytes("vstsdockerbuild:" + GetVSTSPAT()));
                //Todo Polly for retries
                //Todo use microsoft.extensions.logging's ilogger
                //logger.Log(LogLevel.Debug, $"asking for drop manifest  at {manifestUri}");
                
                var manifestreq = new HttpRequestMessage(HttpMethod.Get, manifestUri);
                manifestreq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64EncodedString);
                
                var manifestresponse = await client.SendAsync(manifestreq);
                if (manifestresponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ArgumentException("VSTS drop not found: " + manifestUri.ToString());
                }                
                if (manifestresponse.StatusCode == HttpStatusCode.RedirectMethod ||
                    manifestresponse.StatusCode == HttpStatusCode.Redirect)
                {
                     manifestresponse = await client.GetAsync(manifestresponse.Headers.Location);
                }
                manifestresponse.EnsureSuccessStatusCode();
                string manifestjson = await manifestresponse.Content.ReadAsStringAsync();
                     
                //filter here so we can be case insensitve. manifest url would take a directory but unlike root in drop.exe it is case sensitve.
                var manifest = JsonConvert.DeserializeObject<List<VstsFile>>(manifestjson)
                                    .Where(f => f.Path.StartsWith(relativeroot, StringComparison.OrdinalIgnoreCase));
                
                
                //forget what our limit was but this would be bad for a whole drop. Batch has a certain limit. Should we batch to 500 or lazily get sas urls for only certain directories (those with dockerfiles in them)
                var uniqueblobs = manifest.Select( file => file.Blob.Id).Distinct().Select(id => new  { id =  id});
                var bloblist = JsonConvert.SerializeObject(new { blobs = uniqueblobs });
                var content = new StringContent(bloblist);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                var blobhost = manifestUri.Host.Replace("artifacts", "vsblob");
                var uriBuilder = new UriBuilder(manifestUri.Scheme, blobhost, -1, "defaultcollection/_apis/blob/blobsbatch");
                var queryParameters = HttpUtility.ParseQueryString("");
                queryParameters.Add(APIVersionParam, blobapiversion);
                uriBuilder.Query = queryParameters.ToString();
                
                //logger.Log(LogLevel.Debug, $"asking for sas tokens at {uriBuilder.Uri}");
                var blobreq = new HttpRequestMessage(HttpMethod.Post, uriBuilder.Uri);
                blobreq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64EncodedString);
                blobreq.Content = content;
                var response = await client.SendAsync(blobreq);
                response.EnsureSuccessStatusCode();

                string sasUrlJson = await response.Content.ReadAsStringAsync();
                var blobbatch = JsonConvert.DeserializeObject<BatchBlobResponse>(sasUrlJson);
                
                var urlDictionary = blobbatch.Blobs.ToDictionary(b => b.Id, b => b.Url);
                foreach (var file in manifest)
                {
                    file.Blob.Url = urlDictionary[file.Blob.Id];
                }
                return manifest;
            }
        }   

        private HttpClient _contentClient = new HttpClient();

        //this is just here for unittestsing. not sure I like it
        public Task<Stream> GetContent(string sasurl)
        {
            return _contentClient.GetStreamAsync(sasurl);
        }
    }


    //use vsts drop rest api for manifest and blob and a PAT to grab urls to files and hackily materialize them.
    public class VSTSDropProxy 
    {
        public VSTSDropProxy(string VSTSDropUri)
        {
            //mLogger = logger;
            _dropApi = new RestfulDropApi();
              
            if (!Uri.TryCreate(VSTSDropUri, UriKind.Absolute, out _VSTSDropUri))
            {
                throw new Exception( "Vsts drop path is not a url" + VSTSDropUri);
            }
            var querystring = HttpUtility.ParseQueryString(_VSTSDropUri.Query);
            var rootvalue = querystring?.Get(RootParam);
            if (rootvalue == null)
            {
                //or just make root = /
                throw new Exception( "Vsts drop must contain a ?root= querystring" + _VSTSDropUri.ToString());
            }
            
            _relativeroot = rootvalue.Replace("\\", "/");
            if (!_relativeroot.StartsWith("/"))
                _relativeroot = "/" + _relativeroot;
            if (!_relativeroot.EndsWith("/"))
                _relativeroot = _relativeroot + "/";

            //sigh these went public moths ago. Check if we can use non preview versions
            var manifestapiver = "2.0-preview";
            var blobapiver = "2.1-preview";
        
            //move this to a lazy so we can actually be async?
            IEnumerable<VstsFile> files = null;
            try
            {
                var manifesturi = Munge(_VSTSDropUri, manifestapiver);
                 files = _dropApi.GetVstsManifest(manifesturi, blobapiver, _relativeroot ).Result;
            }
            catch (Exception dropApiException)
            {
                throw new Exception($"Not able to get build manifest please check your build '{VSTSDropUri}' as error:{dropApiException}");
            }

            //dictionary doesn't necesarily make sesne now.
            VstsFilesToDictionary(VSTSDropUri, files);

        }

        //private ILogger mLogger = null;
        private IDropApi _dropApi = null;
        private readonly Uri _VSTSDropUri;
        private readonly string _relativeroot;
        
        private readonly Dictionary<string, string> _pathToUrl = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        
        private static readonly string RootParam = "root";
  
        /// <summary>
        /// Gets the manifest uri from the drop url
        /// </summary>
        /// <param name="dropurl">The drop url.</param>
        /// <returns>The manifest uri.</returns>
        private static Uri Munge(Uri vstsDropUri, string apiversion = "2.0-preview")
        {
            var querystring = HttpUtility.ParseQueryString(vstsDropUri.Query);
            string manifestpath = vstsDropUri.AbsolutePath.Replace("_apis/drop/drops", "_apis/drop/manifests");
            var uriBuilder = new UriBuilder(vstsDropUri.Scheme, vstsDropUri.Host, -1, manifestpath);
            var queryParameters = HttpUtility.ParseQueryString(uriBuilder.Query);
            queryParameters.Add(RestfulDropApi.APIVersionParam,apiversion);
            uriBuilder.Query = queryParameters.ToString();
            
            return uriBuilder.Uri;
        }

        private void VstsFilesToDictionary(string VSTSDropUri, IEnumerable<VstsFile> files)
        {
            if (!files.Any())
            {
                throw new Exception("Encountered empty build drop check your build " + VSTSDropUri);
            }

            if (files.Any(f => f.Invalid())) // if the schema changes catch this ahead of time
            {
                var invalid = files.First(f => f.Invalid());
                throw new Exception("invalid VSTS files for {VSTSDropUri}: {invalid.Path} - {invalid.Blob.Url}");
            }

            //cant do to dictionary because of duplicate binplacing. Cloudbuild can be set to break on this.
            //https://1eswiki.com/wiki/CloudBuild_Duplicate_Binplace_Detection
            foreach (var file in files)
            {
                if (_pathToUrl.ContainsKey(file.Path))
                {
                    continue;
                }
                //ignore blobid/hash -- goodbye caching and verification
                _pathToUrl[file.Path] = file.Blob.Url;
            }

            //mLogger.Log(LogLevel.Info, string.Format("drop rest api returned {0} files", _pathToUrl.Count()));
        }

        private Task<Stream> GetStream(string relativepath)
        {
            var uri = _pathToUrl[Path.Combine(_relativeroot, relativepath.Replace("\\","/"))];
            return _dropApi.GetContent(uri);
        }

        //this is still gross lose caching both witin and outside of builds
        public async Task Materialize(string localDestiantion)
        {
            //slow but we don't care cause we want to move off and have sangam use blobs directly
            foreach (var file in _pathToUrl)
            {
                var localFileName = file.Key.Substring(_relativeroot.Length);
                var localpath = Path.Combine(localDestiantion,localFileName).Replace("/","\\");
                //also not efficient to check directory each time but again this method is a hack.
                Directory.CreateDirectory(Path.GetDirectoryName(localpath));
            }
        
            var downloads = _pathToUrl.Select(async file => {
                var localFileName = file.Key.Substring(_relativeroot.Length);
                var localpath = Path.Combine(localDestiantion,localFileName).Replace("/","\\");
                using (var blob = await GetStream(localFileName))
                using (var fileStream = new FileStream(localpath, FileMode.CreateNew))
                {
                    await blob.CopyToAsync(fileStream);   
                }
            });
            await Task.WhenAll(downloads);
        } 
        

   
    }

    // Helper classes for parsing VSTS drop exe output lowercase to match json output
    public class VstsFile
    {
        public string Path;
        public VstsBlob Blob;

        public bool Invalid()
        {
            return string.IsNullOrWhiteSpace(Path) || string.IsNullOrWhiteSpace(Blob.Url) || string.IsNullOrWhiteSpace(Blob.Id);
        }
    }

    public class VstsBlob
    {
        public string Url;
        public string Id;
    }

    internal sealed class BatchBlobResponse
    {
        /// <summary>
        /// Blob ids and SAS urls.
        /// </summary>
        #pragma warning disable 0649
        public IEnumerable<VstsBlob> Blobs;
        #pragma warning restore 0169
    }
}
