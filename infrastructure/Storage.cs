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

        public string GetBlobEndpoint() 
        {
            return $"https://{_accountName}.blob.core.windows.net";
        }

        public string GetTableEndpoint() 
        {
            return $"https://{_accountName}.table.core.windows.net";
        }

        public string GetQueueEndpoint() 
        {
            return $"https://{_accountName}.queue.core.windows.net";
        }

        public Output<string> GetAccountName() => _storageAccount.Name;

        // public Output<string> CreateBlobContainer(Storage storageAccount, string filePath = "")
        // {
        //     var container = new BlobContainer($"{name}", new BlobContainerArgs
        //     {
        //         AccountName = storageAccount.Name,
        //         ResourceGroupName = _resourceGroup.Name,
        //         PublicAccess = PublicAccess.None,
        //     });

        //     if (!string.IsNullOrEmpty(filePath))
        //     {
        //         var blob = new Blob("zip", new BlobArgs
        //         {
        //             AccountName = storageAccount.Name,
        //             ContainerName = container.Name,
        //             ResourceGroupName = _resourceGroup.Name,
        //             Type = BlobType.Block,
        //             Source = new FileArchive(filePath)
        //         });
        //     }

        //     return Output.Format($"https://{storageAccount.Name}.blob.core.windows.net/{container.Name}");
        // }

        public Output<string> GetStorageAccountId() => _storageAccount.Id;
    }
}