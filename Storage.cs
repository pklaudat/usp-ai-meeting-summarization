using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using System;

namespace UspMeetingSummz
{
    public class Storage
    {
        private string _accountName;
        private ResourceGroup _resourceGroup;
        private StorageAccount _storageAccount;

        public Storage(string name, string location, string env, ResourceGroup resourceGroup)
        {

            this._accountName = LimitStringLength($"{name}{location}{env}", 24);;
            this._resourceGroup = resourceGroup;

            _storageAccount = new StorageAccount(_accountName, new StorageAccountArgs
            {
                AccountName = _accountName,
                ResourceGroupName = _resourceGroup.Name,
                DefaultToOAuthAuthentication = true,
                AllowSharedKeyAccess = false,
                AccessTier = AccessTier.Hot,
                Kind = Kind.StorageV2,
                Sku = new Pulumi.AzureNative.Storage.Inputs.SkuArgs
                {
                    Name = "Standard_LRS"
                }
            });

        }

        public static string LimitStringLength(string input, int maxLength)
        {
            return input.Length <= maxLength ? input : input.Substring(0, maxLength);
        }

        public string GetBlobEndpoint() {
            return $"{_accountName}.blob.windows.core.net";
        }

        public Output<string> GetAccountName() => _storageAccount.Name;

        public void CreateBlobContainer(string name, string filePath="")
        {
            var container = new BlobContainer($"{name}-container", new BlobContainerArgs
            {
                AccountName = _accountName,
                ResourceGroupName = _resourceGroup.Name,
                PublicAccess = PublicAccess.None,
            });

            if (!String.IsNullOrEmpty(filePath))
            {    
                var blob = new Blob("zip", new BlobArgs
                {
                    AccountName = _accountName,
                    ContainerName = container.Name,
                    ResourceGroupName = _resourceGroup.Name,
                    Type = BlobType.Block,
                    Source = new FileArchive(filePath)
                });
            }

        }

        public Output<string> GetStorageAccountId() => _storageAccount.Id;
    }
}