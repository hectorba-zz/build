using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;
using Windows.Web.Syndication;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SimpleReader
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public static ObservableCollection<CustomRSSItem> feedsCollection = new ObservableCollection<CustomRSSItem>();
        public static ObservableCollection<Windows.Storage.ApplicationDataCompositeValue> feedSubscriptionsCollection = new ObservableCollection<Windows.Storage.ApplicationDataCompositeValue>();

        public MainPage()
        {
            this.InitializeComponent();

            //Uncomment to clear all subscriptions on app start
            //ApplicationDataContainer roamingSettings1 = ApplicationData.Current.RoamingSettings;
            //roamingSettings1.DeleteContainer("feedSubscriptions");

            //If we have feed subscriptions in roaming container load them otherwise use defaults
            ApplicationDataContainer roamingSettings = ApplicationData.Current.RoamingSettings;

            if (roamingSettings.Containers.ContainsKey("feedSubscriptions") == true)
            {
                LoadFeedSubscriptionsSettings();
            }
            else
            {
                AddFeedSubscription("Build 2015 Sessions", @"https://channel9.msdn.com/Events/Build/2015/RSS");
            }

            //If undefined create a default, otherwise use stored settings
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            var value = localSettings.Values["#postsToDownload"];
            if (value == null)
            {
                localSettings.Values["#postsToDownload"] = 5;
            }

            //Attach handler to DataChange event
            ApplicationData.Current.DataChanged += new TypedEventHandler<ApplicationData, object>(DataChangeHandler);
        }

        //Add feed subscription to collection given name and URI
        private void AddFeedSubscription(string argFeedName, string argFeedURIString)
        {
            ApplicationDataCompositeValue newFeedSubscription = new ApplicationDataCompositeValue();
            newFeedSubscription.Add("FeedName", argFeedName);
            newFeedSubscription.Add("FeedUri", argFeedURIString);
            feedSubscriptionsCollection.Add(newFeedSubscription);
            SaveFeedSubscriptionsSettings();
        }

        //Save feedSubscriptionsCollection to Roaming Settings
        private void SaveFeedSubscriptionsSettings()
        {
            try
            {
                if (feedSubscriptionsCollection.Count > 0)
                {
                    ApplicationDataContainer roamingSettings = ApplicationData.Current.RoamingSettings;
                    ApplicationDataContainer container = roamingSettings.CreateContainer("feedSubscriptions", ApplicationDataCreateDisposition.Always);

                    foreach (ApplicationDataCompositeValue myApplicationDataCompositeValue in feedSubscriptionsCollection)
                    {
                        string myKey = myApplicationDataCompositeValue["FeedUri"].ToString();
                        //If feed not already defined then add it to the container
                        if (!roamingSettings.Containers["feedSubscriptions"].Values.ContainsKey(myKey))
                        {
                            roamingSettings.Containers["feedSubscriptions"].Values.Add(myKey.ToString(), myApplicationDataCompositeValue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        //Handles change to roaming data when app is running
        private async void DataChangeHandler(ApplicationData appData, object o)
        {
            try
            {
                //Need to update UI from the UI thread
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    LoadFeedSubscriptionsSettings();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        //Populates feedSubscriptionsCollection from Roaming Settings
        private void LoadFeedSubscriptionsSettings()
        {
            try
            {
                ApplicationDataContainer roamingSettings = ApplicationData.Current.RoamingSettings;
                if (roamingSettings.Containers.ContainsKey("feedSubscriptions") == true)
                {
                    feedSubscriptionsCollection.Clear();
                    foreach (var myKVpair in roamingSettings.Containers["feedSubscriptions"].Values.AsEnumerable())
                    {
                        feedSubscriptionsCollection.Add((ApplicationDataCompositeValue)myKVpair.Value);
                    }
                    subscriptionsListView.ItemsSource = feedSubscriptionsCollection;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        //Copies files from LocalCache to SharedLocal
        private async Task CopyLocalCacheToSharedLocal(ApplicationDataCompositeValue argFeedSubscription)
        {
            try
            {
                string feedUriString = argFeedSubscription.ElementAt(1).Value.ToString();
                string folderName = CreateValidFilename(feedUriString);
                IStorageItem myLocalCacheIStorageItem = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync(folderName);
                IStorageItem mySharedLocalIStorageItem = ApplicationData.Current.SharedLocalFolder;

                //If I have cached files and policy allows me to access SharedLocalFolder
                if (myLocalCacheIStorageItem != null && mySharedLocalIStorageItem != null)
                {
                    StorageFolder myLocalCacheFolder = (StorageFolder)myLocalCacheIStorageItem;
                    StorageFolder mySharedLocalFolder = await ApplicationData.Current.SharedLocalFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);

                    //Delete everything in SharedLocal before copying new
                    IReadOnlyList<StorageFile> fileListSharedLocal = await mySharedLocalFolder.GetFilesAsync();
                    foreach (StorageFile file in fileListSharedLocal)
                    {
                        await file.DeleteAsync();
                    }

                    //Copy every file from LocalCache to SharedLocal
                    IReadOnlyList<StorageFile> fileListLocalCache = await myLocalCacheFolder.GetFilesAsync();
                    foreach (StorageFile file in fileListLocalCache)
                    {
                        await file.CopyAsync(mySharedLocalFolder, file.Name, NameCollisionOption.ReplaceExisting);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        #region Helpers

        //Download content for a subscription, save it to LocalCache and add it to the collection
        private async Task CacheFeed(ApplicationDataCompositeValue argFeedSubscription)
        {
            try
            {
                SyndicationClient client = new SyndicationClient();
                client.SetRequestHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/48.0.2564.116 Safari/537.36");
                string feedUriString = argFeedSubscription.ElementAt(1).Value.ToString();
                SyndicationFeed feed = await client.RetrieveFeedAsync(new Uri(feedUriString));
                if (feed != null)
                {
                    feedsCollection.Clear();

                    //Cache my content cache files to LocalCache inside per feed folder
                    //Create folder if it does not exist
                    string folderName = CreateValidFilename(feedUriString);
                    StorageFolder myStorageFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);

                    //Delete any old files in folder
                    IReadOnlyList<StorageFile> fileListLocalCache = await myStorageFolder.GetFilesAsync();
                    foreach (StorageFile file in fileListLocalCache)
                    {
                        await file.DeleteAsync();
                    }

                    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                    int postLimit = int.Parse(localSettings.Values["#postsToDownload"].ToString());

                    //Cache every article
                    foreach (SyndicationItem mySyndicationItem in feed.Items)
                    {
                        //Do not cache more posts that specified by user setting
                        if (feedsCollection.Count == postLimit)
                        {
                            break;
                        }

                        CustomRSSItem myCustomRSSItem = null;

                        //Cache posts
                        string articleFileName = "";
                        Regex rgx1 = new Regex(@".*/(.+)");
                        MatchCollection matches1 = rgx1.Matches(mySyndicationItem.Id);
                        if (matches1.Count > 0)
                        {
                            articleFileName = CreateValidFilename(matches1[0].Groups[1].Value);
                            var xmlDoc = mySyndicationItem.GetXmlDocument(SyndicationFormat.Atom10);
                            IStorageFile myIStorageFile = await myStorageFolder.CreateFileAsync(articleFileName, CreationCollisionOption.ReplaceExisting);
                            await xmlDoc.SaveToFileAsync(myIStorageFile);

                            //Download and cache images
                            BitmapImage myBitmapImage = null;

                            //Get image from thumbnail element
                            var thumbnails = xmlDoc.GetElementsByTagName("thumbnail");
                            if (thumbnails.Count > 1)
                            {
                                string thumbnailUriString = thumbnails[1].Attributes[0].InnerText;
                                if (thumbnailUriString.EndsWith(".jpg"))
                                {
                                    Uri uriForImage = new Uri(thumbnailUriString);
                                    string imageFileName = CreateValidFilename(thumbnailUriString);
                                    if (imageFileName.Length > 50)
                                    {
                                        imageFileName = imageFileName.Substring(imageFileName.Length - 50);
                                    }
                                    HttpClient httpClient = new HttpClient();
                                    httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/48.0.2564.116 Safari/537.36");
                                    IBuffer myIBuffer = await httpClient.GetBufferAsync(uriForImage);
                                    IStorageFile myImageFile = await myStorageFolder.CreateFileAsync(imageFileName, CreationCollisionOption.OpenIfExists);
                                    Stream stream = await myImageFile.OpenStreamForWriteAsync();
                                    stream.Write(myIBuffer.ToArray(), 0, myIBuffer.ToArray().Length);
                                    myBitmapImage = new BitmapImage(new Uri(myImageFile.Path));
                                    await stream.FlushAsync();
                                }
                            }
                            else
                            //Retrieve .jpg from img link from summary text
                            {
                                Regex rgx = new Regex(@"<img.*src=""(.*)""");
                                MatchCollection matches = rgx.Matches(mySyndicationItem.Summary.Text);
                                if (matches.Count > 0)
                                {
                                    string stringForImage = matches[0].Groups[1].Value;
                                    //If Uri has & parameters remove them
                                    stringForImage = RemoveAmpFromUri(stringForImage);
                                    Uri uriForImage = new Uri(stringForImage);

                                    string imageFileName = CreateValidFilename(uriForImage.ToString());
                                    if (imageFileName.Length > 50)
                                    {
                                        imageFileName = imageFileName.Substring(imageFileName.Length - 50);
                                    }
                                    imageFileName = AddExtension(imageFileName);
                                    HttpClient httpClient = new HttpClient();
                                    IBuffer myIBuffer = await httpClient.GetBufferAsync(uriForImage);
                                    IStorageFile myImageFile = await myStorageFolder.CreateFileAsync(imageFileName, CreationCollisionOption.ReplaceExisting);
                                    Stream stream = await myImageFile.OpenStreamForWriteAsync();
                                    stream.Write(myIBuffer.ToArray(), 0, myIBuffer.ToArray().Length);
                                    myBitmapImage = new BitmapImage(new Uri(myImageFile.Path));
                                }
                            }

                            if (myBitmapImage != null)
                            {
                                //If we have an RSSItem and an image, add them to collection
                                myCustomRSSItem = new CustomRSSItem(mySyndicationItem, myBitmapImage);
                                feedsCollection.Add(myCustomRSSItem);
                            }
                        }
                    }
                    articlesListView.ItemsSource = feedsCollection;
                    //Copy what was just cached to SharedLocal
                    Task t = CopyLocalCacheToSharedLocal(argFeedSubscription);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        //Loads cached content for a given subscription
        private async Task LoadFeedCache(ApplicationDataCompositeValue argFeedSubscription)
        {
            try
            {
                feedsCollection.Clear();
                string folderName = CreateValidFilename(argFeedSubscription.ElementAt(1).Value.ToString());
                StorageFolder myStorageFolder = null;
                //If cache folder for subscription exists
                if (Directory.Exists(ApplicationData.Current.LocalCacheFolder.Path + "\\" + folderName))
                {
                    myStorageFolder = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync(folderName);
                }
                else if (Directory.Exists(ApplicationData.Current.SharedLocalFolder.Path + "\\" + folderName))
                {
                    myStorageFolder = await ApplicationData.Current.SharedLocalFolder.GetFolderAsync(folderName);
                }

                if (myStorageFolder != null)
                {
                    IReadOnlyList<StorageFile> fileList = await myStorageFolder.GetFilesAsync();
                    foreach (StorageFile file in fileList)
                    {
                        SyndicationItem mySyndicationItem = new SyndicationItem();
                        BitmapImage myBitmapImage = null;
                        //TODO change .com to something more solid, this will break if we are getting feeds from not .com site
                        //If the file is an article
                        if (file.FileType.ToString() == ".")
                        {
                            Windows.Data.Xml.Dom.XmlDocument myXmlDocument = await Windows.Data.Xml.Dom.XmlDocument.LoadFromFileAsync(file);
                            mySyndicationItem.LoadFromXml(myXmlDocument);

                            var thumbnails = myXmlDocument.GetElementsByTagName("thumbnail");
                            if (thumbnails.Count > 1)
                            {
                                string thumbnailUriString = thumbnails[1].Attributes[1].InnerText;
                                if (thumbnailUriString.EndsWith(".jpg"))
                                {
                                    Uri uriForImage = new Uri(thumbnailUriString);
                                    string imageFileName = CreateValidFilename(thumbnailUriString);
                                    if (imageFileName.Length > 50)
                                    {
                                        imageFileName = imageFileName.Substring(imageFileName.Length - 50);
                                    }
                                    myBitmapImage = new BitmapImage(new System.Uri(myStorageFolder.Path + "\\" + imageFileName));
                                }
                            }
                            else
                            {
                                Regex rgx = new Regex(@"<img.*src=""(.*)""");
                                MatchCollection matches = rgx.Matches(mySyndicationItem.Summary.Text);
                                if (matches.Count > 0)
                                {
                                    string stringForImage = matches[0].Groups[1].Value;
                                    //If Uri has & parameters remove them
                                    stringForImage = RemoveAmpFromUri(stringForImage);
                                    Uri uriForImage = new Uri(stringForImage);

                                    string imageFileName = CreateValidFilename(uriForImage.ToString());
                                    if (imageFileName.Length > 50)
                                    {
                                        imageFileName = imageFileName.Substring(imageFileName.Length - 50);
                                    }
                                    imageFileName = AddExtension(imageFileName);
                                    myBitmapImage = new BitmapImage(new System.Uri(myStorageFolder.Path + "\\" + imageFileName));
                                }
                            }
                            CustomRSSItem myCustomRSSItem = new CustomRSSItem(mySyndicationItem, myBitmapImage);
                            feedsCollection.Insert(0, myCustomRSSItem);
                        }
                    }
                    articlesListView.ItemsSource = feedsCollection;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        //Clean up URI string by removing anything after &
        private string RemoveAmpFromUri(string argString)
        {
            Regex rgx = new Regex(@"(.+?)&.*");
            MatchCollection matches = rgx.Matches(argString);
            if (matches.Count > 0)
            {
                return matches[0].Groups[1].Value;
            }
            else { return argString; }
        }

        //Add .png extension if needed
        private string AddExtension(string argString)
        {
            Regex rgx = new Regex(@"\.[a-z]{3}$");
            MatchCollection matches = rgx.Matches(argString);
            if (matches.Count == 0)
            {
                return argString + ".png";
            }
            else { return argString; }
        }

        //Add entry to FeedSubscription collection given URI
        private async Task AddFeedSubscription(string argFeedURIString)
        {
            ApplicationDataCompositeValue newFeedSubscription = new ApplicationDataCompositeValue();
            //We don't have a name for the feed so we need to fetch it
            Task<string> t = FetchFeedNameAsync(argFeedURIString);
            await t;
            newFeedSubscription.Add("FeedName", t.Result);
            newFeedSubscription.Add("FeedUri", argFeedURIString);
            feedSubscriptionsCollection.Add(newFeedSubscription);
            SaveFeedSubscriptionsSettings();
        }

        //Returns feed name for a given feed Uri
        private async Task<string> FetchFeedNameAsync(string argUriString)
        {
            try
            {
                SyndicationClient client = new SyndicationClient();
                client.SetRequestHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/48.0.2564.116 Safari/537.36");
                SyndicationFeed feed = await client.RetrieveFeedAsync(new Uri(argUriString));
                if (feed != null)
                {
                    return feed.Title.Text;
                }
                else { return "Unknown name"; }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return "Unknown name";
            }
        }

        //Returns a valid file name for a given string by replacing invalid characters with _
        private string CreateValidFilename(string argString)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                argString = argString.Replace(c, '_');
            }
            return argString;
        }

        //Save Uri as stream to disk
        private async Task SaveImageToShared(Uri argUri)
        {
            try
            {
                StorageFolder mySrcStorageFolder;
                if (argUri.Segments[9].StartsWith("LocalCache"))
                {
                    mySrcStorageFolder = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync(argUri.Segments[9]);
                }
                else
                {
                    mySrcStorageFolder = await ApplicationData.Current.SharedLocalFolder.GetFolderAsync(argUri.Segments[9]);
                }

                Stream srcStream = await mySrcStorageFolder.OpenStreamForReadAsync(argUri.Segments[10]);

                StorageFolder myDestStorageFolder = ApplicationData.Current.GetPublisherCacheFolder("LikedPictures");
                string myDestFileName = argUri.Segments.Last();
                StorageFile myDestFile = await myDestStorageFolder.CreateFileAsync(myDestFileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                Stream destStream = await myDestFile.OpenStreamForWriteAsync();

                byte[] buffer = new byte[5000000];
                int read;
                while ((read = srcStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destStream.Write(buffer, 0, read);
                }
                destStream.Flush();
                destStream.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        #endregion Helpers

        #region Event handlers

        //Initialize subscriptionsListView on loading
        private void subscriptionsListView_Loading(FrameworkElement sender, object args)
        {
            this.subscriptionsListView.ItemsSource = feedSubscriptionsCollection;
            if (feedSubscriptionsCollection.Count > 0) { this.subscriptionsListView.SelectedIndex = 0; }
        }

        private void subscriptionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ApplicationDataCompositeValue composite = (ApplicationDataCompositeValue)this.subscriptionsListView.SelectedItem;
                if (composite != null)
                {
                    Task t = LoadFeedCache(composite);
                }
            }
            catch (Exception ex)
            {
                Debug.Write("Exception caught in subscriptionsListView_SelectionChanged " + ex.Message);
            }
        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            ApplicationDataCompositeValue composite = (ApplicationDataCompositeValue)this.subscriptionsListView.SelectedItem;
            if (composite != null)
            {
                Task t = CacheFeed(composite);
            }
        }

        private void Like_Click(object sender, RoutedEventArgs e)
        {
            var myFE = ((FrameworkElement)sender).DataContext;
            CustomRSSItem myCustomRSSItem = (CustomRSSItem)myFE;
            Task t = SaveImageToShared(myCustomRSSItem.myImage.UriSource);
        }

        private void postsToCacheTextBox_Loading(FrameworkElement sender, object args)
        {
            TextBox myTextBox = (TextBox)sender;
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            myTextBox.Text = localSettings.Values["#postsToDownload"].ToString();
        }

        private void postsToCacheTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox myTextBox = (TextBox)sender;
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["#postsToDownload"] = myTextBox.Text.ToString();
        }

        private void buttonAdd_Click(object sender, RoutedEventArgs e)
        {
            Task task = AddFeedSubscription(addFeedTextBox.Text);
            addFeedTextBox.Text = "";
        }

        private void addFeedTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox myTextBox = (TextBox)sender;
            if (myTextBox.Text != "")
            { buttonAdd.IsEnabled = true; }
            else { buttonAdd.IsEnabled = false; }
        }

        #endregion Event handlers
    }
}