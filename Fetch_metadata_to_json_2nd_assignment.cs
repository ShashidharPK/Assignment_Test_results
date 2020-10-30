using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

using Amazon.Runtime;
using ThirdParty.Json.LitJson;
using System.Globalization;
using Amazon.Runtime.Internal.Util;
using Amazon.Util.Internal;
using Amazon.Util;

namespace Amazon.EC2.Util
{

    [Obsolete("This class is deprecated and will be removed in a future release."
              + " Please update your code to use the Amazon.Util.EC2InstanceMetadata class, located in the AWSSDK.Core assembly.")]
    public static class EC2Metadata
    {
        private static string
            EC2_METADATA_SVC = “Address of AWS account which my server hosted”,
            EC2_METADATA_ROOT = EC2_METADATA_SVC + "/latest/meta-data",
            EC2_USERDATA_ROOT = EC2_METADATA_SVC + "/latest/user-data/",
            EC2_APITOKEN_URL = EC2_METADATA_SVC + "latest/api/token";

        private static int
            DEFAULT_RETRIES = 3,
            MIN_PAUSE_MS = 250,
            MAX_RETRIES = 3;

        private static Dictionary<string, string> _cache = new Dictionary<string, string>();

        private static readonly string _userAgent = InternalSDKUtils.BuildUserAgentString(string.Empty);

        public static string AmiId
        {
            get { return FetchData("/ami-id"); }
        }


        public static string AmiLaunchIndex
        {
            get { return FetchData("/ami-launch-index"); }
        }

        public static string AmiManifestPath
        {
            get { return FetchData("/ami-manifest-path"); }
        }

        public static IEnumerable<string> AncestorAmiIds
        {
            get { return GetItems("/ancestor-ami-ids"); }
        }

        public static string Hostname
        {
            get { return FetchData("/hostname"); }
        }

        public static string InstanceAction
        {
            get { return FetchData("/instance-action"); }
        }

        public static string InstanceId
        {
            get { return FetchData("/instance-id"); }
        }

        public static string InstanceType
        {
            get { return FetchData("/instance-type"); }
        }

        public static string KernelId
        {
            get { return GetData("kernel-id"); }
        }


        public static string LocalHostname
        {
            get { return FetchData("/local-hostname"); }
        }

        public static string MacAddress
        {
            get { return FetchData("/mac"); }
        }

        public static string PrivateIpAddress
        {
            get { return FetchData("/local-ipv4"); }
        }


        public static string AvailabilityZone
        {
            get { return FetchData("/placement/availability-zone"); }
        }


        public static IEnumerable<string> ProductCodes
        {
            get { return GetItems("/product-codes"); }
        }

        public static string PublicKey
        {
            get { return FetchData("/public-keys/0/openssh-key"); }
        }


        public static string RamdiskId
        {
            get { return FetchData("/ramdisk-id"); }
        }

        public static string ReservationId
        {
            get { return FetchData("/reservation-id"); }
        }


        public static IEnumerable<string> SecurityGroups
        {
            get { return GetItems("/security-groups"); }
        }

        public static IAMInfo IAMInstanceProfileInfo
        {
            get
            {
                var json = GetData("/iam/info");
                if (null == json)
                    return null;
                IAMInfo info;
                try
                {
                    info = JsonMapper.ToObject<IAMInfo>(json);
                }
                catch
                {
                    info = new IAMInfo { Code = "Failed", Message = "Could not parse response from metadata service." };
                }
                return info;
            }
        }

        public static IDictionary<string, IAMSecurityCredential> IAMSecurityCredentials
        {
            get
            {
                var list = GetItems("/iam/security-credentials");
                if (list == null)
                    return null;

                var creds = new Dictionary<string, IAMSecurityCredential>();
                foreach (var item in list)
                {
                    var json = GetData("/iam/security-credentials/" + item);
                    try
                    {
                        var cred = JsonMapper.ToObject<IAMSecurityCredential>(json);
                        creds[item] = cred;
                    }
                    catch
                    {
                        creds[item] = new IAMSecurityCredential { Code = "Failed", Message = "Could not parse response from metadata service." };
                    }
                }

                return creds;
            }
        }

        public static IDictionary<string, string> BlockDeviceMapping
        {
            get
            {
                var keys = GetItems("/block-device-mapping");
                if (keys == null)
                    return null;

                var mapping = new Dictionary<string, string>();
                foreach (var key in keys)
                {
                    mapping[key] = GetData("/block-device-mapping/" + key);
                }

                return mapping;
            }
        }


        public static IEnumerable<NetworkInterface> NetworkInterfaces
        {
            get
            {
                var macs = GetItems("/network/interfaces/macs/");
                if (macs == null)
                    return null;

                var interfaces = new List<NetworkInterface>();
                foreach (var mac in macs)
                {
                    interfaces.Add(new NetworkInterface(mac.Trim('/')));
                }
                return interfaces;
            }
        }

        public static string UserData
        {
            get
            {
                return GetData(EC2_USERDATA_ROOT);
            }
        }


        public static IEnumerable<string> GetItems(string path)
        {
            return GetItems(path, DEFAULT_RETRIES, false);
        }


        public static string GetData(string path)
        {
            return GetData(path, DEFAULT_RETRIES);
        }


        public static string GetData(string path, int tries)
        {
            var items = GetItems(path, tries, true);
            if (items != null && items.Count > 0)
                return items[0];
            return null;
        }

        public static IEnumerable<string> GetItems(string path, int tries)
        {
            return GetItems(path, tries, false);
        }

        private static string FetchData(string path)
        {
            return FetchData(path, false);
        }

        private static string FetchData(string path, bool force)
        {
            try
            {
                if (force || !_cache.ContainsKey(path))
                    _cache[path] = GetData(path);

                return _cache[path];
            }
            catch
            {
                return null;
            }
        }

        private static List<string> GetItems(string path, int tries, bool slurp)
        {
            return GetItems(path, tries, slurp, null);
        }

        private static List<string> GetItems(string path, int tries, bool slurp, string token)
        {
            var items = new List<string>();
            //For all meta-data queries we need to fetch an api token to use. In the event a
            //token cannot be obtained we will fallback to not using a token.
            Dictionary<string, string> headers = null;
            if (token == null)
            {
                token = Amazon.Util.EC2InstanceMetadata.FetchApiToken();
            }

            if (!string.IsNullOrEmpty(token))
            {
                headers = new Dictionary<string, string>();
                headers.Add(HeaderKeys.XAwsEc2MetadataToken, token);
            }


            try
            {
                if (!Amazon.Util.EC2InstanceMetadata.IsIMDSEnabled)
                {
                    throw new IMDSDisabledException();
                }

                HttpWebRequest request;
                if (path.StartsWith("http", StringComparison.Ordinal))
                    request = WebRequest.Create(path) as HttpWebRequest;
                else
                    request = WebRequest.Create(EC2_METADATA_ROOT + path) as HttpWebRequest;

                request.Timeout = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
                request.UserAgent = _userAgent;
                if(headers != null)
                {
                    foreach(var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                using (var response = request.GetResponse())
                {
                    using (var stream = new StreamReader(response.GetResponseStream()))
                    {
                        if (slurp)
                            items.Add(stream.ReadToEnd());
                        else
                        {
                            string line;
                            do
                            {
                                line = stream.ReadLine();
                                if (line != null)
                                    items.Add(line.Trim());
                            }
                            while (line != null);
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                var response = wex.Response as HttpWebResponse;
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        EC2InstanceMetadata.ClearTokenFlag();
                        Logger.GetLogger(typeof(Amazon.EC2.Util.EC2Metadata)).Error(wex, "EC2 Metadata service returned unauthorized for token based secure data flow.");
                        throw;
                    }
                }

                if (tries <= 1)
                {
                    Logger.GetLogger(typeof(Amazon.EC2.Util.EC2Metadata)).Error(wex, "Unable to contact EC2 Metadata service.");
                    return null;
                }

                PauseExponentially(tries);
                return GetItems(path, tries - 1, slurp, token);
            }
            catch (IMDSDisabledException)
            {
                // Keep this behavior identical to when HttpStatusCode.NotFound is returned.
                return null;
            }

            return items;
        }

        private static void PauseExponentially(int tries)
        {
            tries = Math.Min(tries, MAX_RETRIES);
            var pause = (int)(Math.Pow(2, DEFAULT_RETRIES - tries) * MIN_PAUSE_MS);
            Thread.Sleep(pause < MIN_PAUSE_MS ? MIN_PAUSE_MS : pause);
        }

#if !NETSTANDARD
        [Serializable]
#endif
        private class IMDSDisabledException : InvalidOperationException { };
    }

    [Obsolete("This class is deprecated and will be removed in a future release."
              + " Please update your code to use the Amazon.Util.IAMInstanceProfileMetadata class, located in the AWSSDK.Core assembly.")]
    public class IAMInfo
    {

        public string Code { get; set; }

        public string Message { get; set; }

        public DateTime LastUpdated { get; set; }

        public string InstanceProfileArn { get; set; }

        public string InstanceProfileId { get; set; }
    }


    [Obsolete("This class is deprecated and will be removed in a future release."
              + " Please update your code to use the Amazon.Util.IAMSecurityCredentialMetadata class, located in the AWSSDK.Core assembly.")]
    public class IAMSecurityCredential
    {

        public string Code { get; set; }

        public string Message { get; set; }

        public DateTime LastUpdated { get; set; }

        public string Type { get; set; }

        public string AccessKeyId { get; set; }

        public string SecretAccessKey { get; set; }

        public string Token { get; set; }

        public DateTime Expiration { get; set; }
    }

    [Obsolete("This class is deprecated and will be removed in a future release."
              + " Please update your code to use the Amazon.Util.NetworkInterfaceMetadata class, located in the AWSSDK.Core assembly.")]
    public class NetworkInterface
    {
        private  string _path;
        private string _mac;

        private IEnumerable<string> _availableKeys;
        private Dictionary<string, string> _data = new Dictionary<string, string>();

        private NetworkInterface() { }

        public NetworkInterface(string macAddress)
        {
            _mac = macAddress;
            _path = string.Format(CultureInfo.InvariantCulture, "/network/interfaces/macs/{0}/", _mac);
        }

        public string MacAddress
        {
            get { return _mac; }
        }

        public string OwnerId
        {
            get { return GetData("owner-id"); }
        }

        public string Profile
        {
            get { return GetData("profile"); }
        }

        public string LocalHostname
        {
            get { return GetData("local-hostname"); }
        }

        public IEnumerable<string> LocalIPv4s
        {
            get { return GetItems("local-ipv4s"); }
        }

        public string PublicHostname
        {
            get { return GetData("public-hostname"); }
        }

        public IEnumerable<string> PublicIPv4s
        {
            get { return GetItems("public-ipv4s"); }
        }

        public IEnumerable<string> SecurityGroups
        {
            get { return GetItems("security-groups"); }
        }

        public IEnumerable<string> SecurityGroupIds
        {
            get { return GetItems("security-group-ids"); }
        }

        public string SubnetId
        {
            get { return GetData("subnet-id"); }
        }

        public string SubnetIPv4CidrBlock
        {
            get { return GetData("subnet-ipv4-cidr-block"); }
        }

        public string VpcId
        {
            get { return GetData("vpc-id"); }
        }


        public IEnumerable<string> GetIpV4Association(string publicIp)
        {
            return EC2Metadata.GetItems(string.Format(CultureInfo.InvariantCulture, "{0}ipv4-associations/{1}", _path, publicIp));
        }

        private string GetData(string key)
        {
            if (_data.ContainsKey(key))
                return _data[key];

            if (null == _availableKeys)
                _availableKeys = EC2Metadata.GetItems(_path);

            if (_availableKeys.Contains(key))
            {
                _data[key] = EC2Metadata.GetData(_path + key);
                return _data[key];
            }
            else
                return null;
        }

        private IEnumerable<string> GetItems(string key)
        {
            if (null == _availableKeys)
                _availableKeys = EC2Metadata.GetItems(_path);

            if (_availableKeys.Contains(key))
            {
                return EC2Metadata.GetItems(_path + key);
            }
            else
                return new List<string>();
        }
    }

}
