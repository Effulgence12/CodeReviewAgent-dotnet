using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShopDemo
{
    // 示例：一个故意包含多处问题的订单服务，用于演示代码审查 Agent 的真实分析能力。
    public class OrderService
    {
        private List<Order> orders = new List<Order>();

        // 公共方法命名不规范（应为 PascalCase），且 async 方法内没有 await
        public async Task<decimal> calculateTotal(int orderId)
        {
            var order = orders.Find(o => o.Id == orderId);
            decimal total = 0;
            foreach (var item in order.Items)
            {
                total = total + item.Price * item.Quantity;
            }

            // 魔法数字：税率硬编码
            total = total * 1.13m;

            if (total > 9999)
            {
                total = total - 500;
            }
            return total;
        }

        public void SaveOrder(Order order)
        {
            try
            {
                orders.Add(order);
                Database.Persist(order);
            }
            catch (Exception)
            {
                // 空 catch：异常被吞掉，调用方无从得知失败
            }
        }

        // TODO: 这里需要补充并发控制
        public Order GetOrder(int id)
        {
            return orders.Find(o => o.Id == id);
        }
    }

    public class Order
    {
        public int Id { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public static class Database
    {
        public static void Persist(Order order) { }
    }
}
