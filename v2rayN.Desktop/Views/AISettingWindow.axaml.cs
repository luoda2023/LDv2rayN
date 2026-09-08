using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using ServiceLib.ViewModels;

namespace v2rayN.Desktop.Views;

public partial class AISettingWindow : WindowBase<AISettingViewModel>
{
    public AISettingWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.AIEnabled, v => v.togAIEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ApiUrl, v => v.txtApiUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ApiKey, v => v.txtApiKey.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ModelId, v => v.txtModelId.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AiGroupRemarks, v => v.txtAiGroupRemarks.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SearchIntervalMinutes, v => v.txtSearchInterval.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MaxNodesPerSearch, v => v.txtMaxNodes.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TestFetchCmd, v => v.btnTestFetch).DisposeWith(disposables);

            ViewModel.RequestClose += (s, e) => Close();
        });
    }
}
