using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SimpleLockScreen
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public static ObservableCollection<BitmapImage> imageCollection = new ObservableCollection<BitmapImage>();

        public MainPage()
        {
            this.InitializeComponent();
            Task t = LoadImages();
        }

        private async Task LoadImages()
        {
            try
            {
                imageCollection.Clear();
                //Load images from PublisherCache
                StorageFolder publisherLikedPictures = ApplicationData.Current.GetPublisherCacheFolder("LikedPictures");
                IReadOnlyList<StorageFile> fileList = await publisherLikedPictures.GetFilesAsync();
                foreach (StorageFile file in fileList)
                {
                    BitmapImage myBitmapImage = new BitmapImage(new Uri(file.Path));
                    imageCollection.Add(myBitmapImage);
                }

                //If there are no images in PublisherCache try to load them from SharedLocal
                if (imageCollection.Count == 0)
                {
                    StorageFolder sharedLikedPictures = ApplicationData.Current.SharedLocalFolder; ;
                    if (sharedLikedPictures != null)
                    {
                        fileList = await sharedLikedPictures.GetFilesAsync();

                        foreach (StorageFile file in fileList)
                        {
                            BitmapImage myBitmapImage = new BitmapImage(new Uri(file.Path));
                            imageCollection.Add(myBitmapImage);
                        }
                    }
                }

                imagesListView.ItemsSource = imageCollection;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            Task t = LoadImages();
        }
    }
}