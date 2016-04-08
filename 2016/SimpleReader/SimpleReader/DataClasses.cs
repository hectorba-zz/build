using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Syndication;

namespace SimpleReader
{
    public class CustomRSSItem
    {
        public SyndicationItem mySyndicationItem { get; set; }
        public BitmapImage myImage { get; set; }

        public CustomRSSItem(SyndicationItem SI, BitmapImage BI)
        {
            mySyndicationItem = SI;
            myImage = BI;
        }
    }
}