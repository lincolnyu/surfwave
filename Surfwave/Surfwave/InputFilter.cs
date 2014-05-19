using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace Surfwave
{
    public delegate bool InputFilter(UIElement surfaceElement, object sender, PointerRoutedEventArgs e);
}
