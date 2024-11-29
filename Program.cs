using Newtonsoft.Json;

namespace redmineApi
{
    static class Program
    {
        public static void Main(string[] args)
        {
            // Redmine 設定
            var redmineFactory = new RedmineFactory("tcbbank03");
            var list = redmineFactory.GetIssues();
            foreach (var item in list)
            {
                Console.WriteLine(JsonConvert.SerializeObject(item));
            }

        }
    }
}