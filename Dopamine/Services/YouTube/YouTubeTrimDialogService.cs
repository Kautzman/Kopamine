using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Dialog;
using Dopamine.ViewModels.Common;
using Dopamine.Views.Common;
using Prism.Ioc;

namespace Dopamine.Services.YouTube
{
    public class YouTubeTrimDialogService : IYouTubeTrimDialogService
    {
        private const int CutIconGlyph = 0xE8C6;
        private const int IconSize = 16;
        private const int DialogWidth = 1000;
        private const int DialogHeight = 720;

        private readonly IContainerProvider container;
        private readonly IDialogService dialogService;

        public YouTubeTrimDialogService(IContainerProvider container, IDialogService dialogService)
        {
            this.container = container;
            this.dialogService = dialogService;
        }

        public bool ShowTrimDialog(YouTubeVideo video, string audioPath)
        {
            YouTubeTrimViewModel viewModel = this.container.Resolve<YouTubeTrimViewModel>();
            viewModel.Initialize(video, audioPath);

            YouTubeTrim view = this.container.Resolve<YouTubeTrim>();
            view.DataContext = viewModel;

            try
            {
                // The dialog's OK button is the bottom-right "Save" button; SaveAsync keeps the
                // dialog open (returns false) when validation or saving fails.
                return this.dialogService.ShowCustomDialog(
                    CutIconGlyph,
                    IconSize,
                    ResourceUtils.GetString("Language_YouTube_Trim_And_Save"),
                    view,
                    DialogWidth,
                    DialogHeight,
                    true,
                    false,
                    true,
                    true,
                    ResourceUtils.GetString("Language_Save"),
                    ResourceUtils.GetString("Language_Cancel"),
                    viewModel.SaveAsync);
            }
            finally
            {
                viewModel.Dispose();
            }
        }
    }
}
