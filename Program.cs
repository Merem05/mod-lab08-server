using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using ScottPlot;

namespace lab08_server
{
    public class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }

    public class Metrics
    {
        public double lambda;
        public double mu;
        public double P0;
        public double Pn;
        public double Q;
        public double A;
        public double k;
    }

    public class Server
    {
        private struct PoolRecord
        {
            public Thread thread;
            public bool in_use;
        }

        private PoolRecord[] pool;
        private object threadLock = new object();
        public int channels;

        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;

        public int freeSamples = 0;
        public int totalSamples = 0;

        public long totalTime = 0;
        public int timeCount = 0;
        public double avgTime = 0;
        private int serviceTimeMs;

        public Server(int n, int serviceTimeMs = 50)
        {
            channels = n;
            this.serviceTimeMs = serviceTimeMs;
            pool = new PoolRecord[n];
            for (int i = 0; i < n; i++)
                pool[i].in_use = false;
        }

        public void CheckFree()
        {
            lock (threadLock)
            {
                totalSamples++;
                bool allFree = true;
                for (int i = 0; i < channels; i++)
                {
                    if (pool[i].in_use)
                    {
                        allFree = false;
                        break;
                    }
                }
                if (allFree) freeSamples++;
            }
        }

        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;

                for (int i = 0; i < channels; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        processedCount++;
                        return;
                    }
                }
                rejectedCount++;
            }
        }

        private void Answer(object arg)
        {
            int id = (int)arg;
            DateTime start = DateTime.Now;
            Thread.Sleep(serviceTimeMs);
            DateTime end = DateTime.Now;

            double ms = (end - start).TotalMilliseconds;

            lock (threadLock)
            {
                totalTime += (long)ms;
                timeCount++;
                avgTime = (double)totalTime / timeCount;

                for (int i = 0; i < channels; i++)
                {
                    if (pool[i].thread == Thread.CurrentThread)
                    {
                        pool[i].in_use = false;
                        break;
                    }
                }
            }
        }

        public void ResetStats()
        {
            requestCount = 0;
            processedCount = 0;
            rejectedCount = 0;
            freeSamples = 0;
            totalSamples = 0;
            totalTime = 0;
            timeCount = 0;
            avgTime = 0;
        }
    }

    public class Client
    {
        private Server server;

        public Client(Server server)
        {
            this.server = server;
            this.request += server.proc;
        }

        public void send(int id)
        {
            procEventArgs args = new procEventArgs();
            args.id = id;
            OnProc(args);
        }

        protected virtual void OnProc(procEventArgs e)
        {
            EventHandler<procEventArgs> handler = request;
            if (handler != null)
                handler(this, e);
        }

        public event EventHandler<procEventArgs> request;
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string projectDir = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName;
            string solutionDir = Directory.GetParent(projectDir).FullName;
            string resultDir = Path.Combine(solutionDir, "result");
            Directory.CreateDirectory(resultDir);

            Console.WriteLine("СМО с отказамаи\n");

            int n = 3;
            int req = 200;
            int serviceTimeMs = 500;
            double mu = 1000.0 / serviceTimeMs;

            int[] delays = { 100, 120, 140, 160, 180, 200, 220, 240, 260, 280 };

            List<double> lam = new List<double>();
            List<double> P0_exp_list = new List<double>();
            List<double> P0_th_list = new List<double>();
            List<double> Pn_exp_list = new List<double>();
            List<double> Pn_th_list = new List<double>();
            List<double> Q_exp_list = new List<double>();
            List<double> Q_th_list = new List<double>();
            List<double> A_exp_list = new List<double>();
            List<double> A_th_list = new List<double>();
            List<double> k_exp_list = new List<double>();
            List<double> k_th_list = new List<double>();

            Console.WriteLine($"Каналов: {n}");
            Console.WriteLine($"Заявок: {req}");
            Console.WriteLine($"Время обработки: {serviceTimeMs} мс, мю = {mu:F2} заявок/сек\n");

            string resultsPath = Path.Combine(resultDir, "results.txt");
            using (StreamWriter file = new StreamWriter(resultsPath, false, System.Text.Encoding.UTF8))
            {
                file.WriteLine("Результаты");
                file.WriteLine($"Каналов: {n}, Заявок: {req}, мю = {mu:F2}");
                file.WriteLine();
                file.WriteLine("λ\tP0(теор)\t\tP0(эксп)\tPотк(теор)\t\tPотк(эксп)\tQ(теор)\t\tQ(эксп)\tA(теор)\t\tA(эксп)\tk(теор)\t\tk(эксп)");
                file.WriteLine("------------------------------------------------------------------------------------------------------------------------");

                for (int i = 0; i < delays.Length; i++)
                {
                    int delay = delays[i];
                    double lambda = 1000.0 / delay;

                    Console.Write($"λ = {lambda:F2} (задержка {delay} мс)... ");

                    Server server = new Server(n, serviceTimeMs);
                    Client client = new Client(server);

                    DateTime startTime = DateTime.Now;

                    for (int id = 1; id <= req; id++)
                    {
                        client.send(id);
                        Thread.Sleep(delay);
                        server.CheckFree();
                    }

                    Thread.Sleep(serviceTimeMs * 2);

                    DateTime endTime = DateTime.Now;
                    double experimentTime = (endTime - startTime).TotalSeconds;

                    Metrics exp = new Metrics();
                    exp.lambda = server.requestCount / experimentTime;
                    exp.mu = 1000.0 / server.avgTime;
                    exp.P0 = (double)server.freeSamples / server.totalSamples;
                    exp.Pn = (double)server.rejectedCount / server.requestCount;
                    exp.Q = 1 - exp.Pn;
                    exp.A = exp.lambda * exp.Q;
                    exp.k = exp.A / exp.mu;

                    Metrics theor = CalculateTheory(lambda, mu, n);

                    lam.Add(lambda);
                    P0_exp_list.Add(exp.P0);
                    P0_th_list.Add(theor.P0);
                    Pn_exp_list.Add(exp.Pn);
                    Pn_th_list.Add(theor.Pn);
                    Q_exp_list.Add(exp.Q);
                    Q_th_list.Add(theor.Q);
                    A_exp_list.Add(exp.A);
                    A_th_list.Add(theor.A);
                    k_exp_list.Add(exp.k);
                    k_th_list.Add(theor.k);

                    Console.WriteLine($"P0(эксп)={exp.P0:F4}, Pотк(эксп)={exp.Pn:F4}, Pотк(теор)={theor.Pn:F4}");

                    file.WriteLine($"{lambda:F2}\t\t{theor.P0:F4}\t{exp.P0:F4}\t\t{theor.Pn:F4}\t{exp.Pn:F4}\t\t{theor.Q:F4}\t{exp.Q:F4}\t\t{theor.A:F2}\t{exp.A:F2}\t\t{theor.k:F2}\t{exp.k:F2}");

                }
            }

            Console.WriteLine($"\nРезультаты в {resultsPath}\n");

            double[] x = lam.ToArray();

            DrawGraph(x, P0_th_list.ToArray(), P0_exp_list.ToArray(),
                "Интенсивность λ (заявок/сек)", "Вероятность простоя P0",
                "График 1: Зависимость P0(λ)", Path.Combine(resultDir, "p-1.png"));

            DrawGraph(x, Pn_th_list.ToArray(), Pn_exp_list.ToArray(),
                "Интенсивность λ (заявок/сек)", "Вероятность отказа Pотк",
                "График 2: Зависимость Pотк(λ)", Path.Combine(resultDir, "p-2.png"));

            DrawGraph(x, Q_th_list.ToArray(), Q_exp_list.ToArray(),
                "Интенсивность λ (заявок/сек)", "Относительная пропускная способность Q",
                "График 3: Зависимость Q(λ)", Path.Combine(resultDir, "p-3.png"));

            DrawGraph(x, A_th_list.ToArray(), A_exp_list.ToArray(),
                "Интенсивность λ (заявок/сек)", "Абсолютная пропускная способность A",
                "График 4: Зависимость A(λ)", Path.Combine(resultDir, "p-4.png"));

            DrawGraph(x, k_th_list.ToArray(), k_exp_list.ToArray(),
                "Интенсивность λ (заявок/сек)", "Среднее число занятых каналов k",
                "График 5: Зависимость k(λ)", Path.Combine(resultDir, "p-5.png"));

            Console.WriteLine("\nГрафики созданы");
            Console.ReadKey();
        }

        static Metrics CalculateTheory(double lambda, double mu, int n)
        {
            Metrics m = new Metrics();
            m.lambda = lambda;
            m.mu = mu;

            double rho = lambda / mu;
            double sum = 0;
            for (int k = 0; k <= n; k++)
                sum += Math.Pow(rho, k) / Factorial(k);

            m.P0 = 1.0 / sum;
            m.Pn = (Math.Pow(rho, n) / Factorial(n)) * m.P0;
            m.Q = 1 - m.Pn;
            m.A = lambda * m.Q;
            m.k = m.A / mu;

            return m;
        }

        static void DrawGraph(double[] x, double[] yTheory, double[] yExp,
            string xLabel, string yLabel, string title, string filename)
        {
            var plot = new Plot();
            plot.XLabel(xLabel);
            plot.YLabel(yLabel);
            plot.Title(title);

            var theory = plot.Add.Scatter(x, yTheory);
            theory.Label = "Теория";
            theory.Color = new ScottPlot.Color(0, 0, 255);

            var experiment = plot.Add.Scatter(x, yExp);
            experiment.Label = "Эксперимент";
            experiment.Color = new ScottPlot.Color(255, 0, 0);

            plot.ShowLegend();
            plot.SavePng(filename, 1920, 1080);
        }

        static int Factorial(int n)
        {
            int result = 1;
            for (int i = 2; i <= n; i++)
                result *= i;
            return result;
        }
    }
}