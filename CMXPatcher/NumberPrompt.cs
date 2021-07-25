﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CMXPatcher
{
    public static class NumberPrompt
    {
        public static int ShowDialog(string type)
        {
            Form prompt = new Form();
            prompt.Width = 300;
            prompt.Height = 100;
            prompt.Text = $"Enter a {type} id:";
            NumericUpDown inputBox = new NumericUpDown() { Left = 100, Top = 5, Width = 100, Maximum = 999999, Minimum = -1, Value = -1 };
            Button confirmation = new Button() { Text = "Ok", Dock = DockStyle.Bottom };
            confirmation.Click += (sender, e) => { prompt.Close(); };

            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(inputBox);
            prompt.ShowDialog();

            return (int)inputBox.Value;
        }
    }
}
