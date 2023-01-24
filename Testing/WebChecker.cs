using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lomont.Algorithms;
using Lomont;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools.V106.CSS;
using OpenQA.Selenium.Support.UI;

namespace Testing
{
    internal class WebChecker
    {

        public static void RunIt()
        {
            // do web posit calc scraping
            // seems lots of places I got posit code and tested does not match this place
            // place is central posit hub 
            void TestWeb()
            {
                var wb = new Testing.WebChecker();

                var answers = new List<(string command, string ans)>();
                var lhs = new Posit(8, 0);
                var rhs = new Posit(8, 0);
                for (var i = 0U; i < 256; ++i)
                    for (var j = 0U; j < 256; ++j)
                    {
                        lhs.Bits = i;
                        rhs.Bits = j;
                        var a1 = lhs + rhs;
                        //if (lhs.Value == 0.0 || rhs.Value == 0.0 || a1.Value == 0.0) continue; // skip these for now
                        var command = $"{lhs} + {rhs} = ".Replace("P'", "");
                        answers.Add((command, a1.ToString().Replace("P'", "")));
                    }
                answers.Shuffle();
                var commands = answers.Select(p => p.command).ToList();

                int ansIndex = 0;
                int ansTry = 0;
                int errors = 0;
                int success = 0;
                using var f = File.CreateText("p8fails.txt");
                var sw = new Stopwatch();
                sw.Start();
                wb.Start("posit8");
                wb.Check(commands, ScrapeAction);

                bool ScrapeAction()
                {
                    // (string p8, string p16, string f16, string p32, string f32, string f64) vals
                    var a = answers[ansIndex].ans;

                    // 0 => 0.0, 1 => 1.0, etc.
                    //if (a == "0") a = "0.0";

                    string p8 = "";
                    int pass = 0;
                    while (pass < 40) // total time?
                    {
                        p8 = wb.Read("posit8");
                        if (p8.EndsWith(".0"))
                            p8 = p8[0..^2];
                        if (p8 == a) break; // we got it? what about times where was 0 and didn't update on web?
                        Thread.Sleep(15);
                        ++pass;
                    }

                    // we try a few times:
                    if (p8 != a)
                    {
                        ansTry++;
                        if (ansTry < 10)
                            return false; // retry
                    }

                    ansTry = 0;


                    if (p8 != a)
                    {
                        Console.WriteLine($"ERROR: {p8} != {a} from {answers[ansIndex]}");
                        f.WriteLine(answers[ansIndex]);
                        f.Flush();
                        ++errors;
                    }
                    else
                    {
                        //Console.WriteLine("SUCCESS");
                        ++success;
                    }

                    ++ansIndex;
                    if ((ansIndex % 5) == 0)
                    {
                        Console.WriteLine(
                            $"{ansIndex}/{answers.Count}, {errors} errors, {success} successes, elapsed {sw.Elapsed}");
                    }

                    return true;
                }
            }

            TestWeb();

            return;

        }

        public void Start(string valueType)
        {
            driver = new ChromeDriver();
            driver.Navigate().GoToUrl("https://posithub.org/widget/calculator/");

            SelectElement dropDown = new SelectElement(driver.FindElement(By.Id("ptype")));
            //Posit8,16,32,32,float16,32,64
            dropDown.SelectByValue(valueType);

            /* button names to click all have "btn" in class
            btn num (text 0,1,..,9)
            btn clear, negative, divide, multiply, subtract, add, equals
            text '.', 
             */

            // users = browser.find_elements_by_xpath('*//button[text()='Follow']')
            // where to type
            buttons =
                driver.FindElements(By.ClassName("btn"));
            //By.XPath("*button[text()=]")); //"/div[3]/form/div[1]/div[1]/div[1]/div/div[2]/input"));
            //foreach (var element2 in buttons)
            //    Console.WriteLine(element2.Text);

            posits = driver.FindElements(By.ClassName("posit-text"));
            //foreach (var element2 in posits)
            //    Console.WriteLine(element2.Text);

            clr = buttons.FirstOrDefault(b => b.Text == "AC");
            neg = buttons.FirstOrDefault(b => b.Text == "+/-");
        }

        private ReadOnlyCollection<IWebElement> buttons;
        private ReadOnlyCollection<IWebElement> posits;
        private IWebElement? clr;
        private IWebElement? neg;
        private IWebDriver driver;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueType">posit8,16,32, float16,32,64</param>
        /// <param name="executeTexts"></param>
        public void Check(
            IList<string> executeTexts,
            Func<bool> answerProcessed
            )
        {

            foreach (var line in executeTexts)
            {
                while (true)
                {

                    clr.Click();
                    clr.Click();
                    driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromMicroseconds(40);
                    foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (token.StartsWith("-") || Char.IsDigit(token[0]))
                        {
                            // enter number
                            foreach (var c in token)
                            {
                                if (c != '-') Click(c);
                            }

                            if (token.StartsWith('-'))
                                neg.Click();
                        }
                        else
                        {
                            var t = token;
                            if (t == "NaR" || t == "NaN")
                                t = "1/0=";
                            if (t == "/") t = "'÷";
                            Trace.Assert(t == "1/0" || t.Length == 1, $"Error: token is ${t}");
                            foreach (var c in token)
                                Click(c);
                        }
                    }

                    void Click(char ch)
                    {
                        var bt = buttons.FirstOrDefault(b => b.Text == ch.ToString());
                        if (bt == null)
                            Console.WriteLine($"ERROR! cannot find button '{ch}'");
                        else
                            bt.Click();
                    }

                    //Thread.Sleep(500);
                    //driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(30);
                    if (answerProcessed()) break; // done with loop, else try again
                }
            }
            // read values
            // todo


            /* 
            AC +/- % ÷ 7 8 9 x 4 5 6 - 1 2 3 + 0 . = 
             */
        }
        public string Read(string valueType)
        {
            return valueType switch
            {
                "posit8" => posits[0].Text,
                _ => throw new NotImplementedException()
            };
            //while (posits[5].Text == "0")
            //{
            //
            //}

            //action((
            //    posits[0].Text, // posit8
            //    posits[1].Text, // posit16
            //    posits[2].Text, // float16
            //    posits[3].Text, // posit32
            //    posits[4].Text, // float32
            //    posits[5].Text  // float64
            //));
            ////foreach (var element2 in posits)
            ////    Console.WriteLine(element2.Text);

            while (posits[5].Text != "0")
            {

            }
            //Thread.Sleep(200);
        }


    }
}
