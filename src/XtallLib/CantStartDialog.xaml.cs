namespace XtallLib
{
    public partial class CantStartDialog
    {
        public CantStartDialog(CantStartViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}
