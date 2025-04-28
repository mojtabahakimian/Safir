// File: Client/Services/ShoppingCartService.cs
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Visitory; // For VISITOR_CUSTOMERS
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging; // Added for logging
using Safir.Client.Services; // Added for LookupApiService (optional)

namespace Safir.Client.Services
{
    public class ShoppingCartService
    {
        public VISITOR_CUSTOMERS? CurrentCustomer { get; private set; }
        public List<CartItem> Items { get; private set; } = new List<CartItem>();
        public int? CurrentAnbarCode { get; private set; } // Keep this if needed for ItemCard logic
        public event Action? CartChanged;
        private readonly ILogger<ShoppingCartService>? _logger;
        private readonly LookupApiService? _lookupService;

        public ShoppingCartService(ILogger<ShoppingCartService>? logger = null, LookupApiService? lookupService = null)
        {
            _logger = logger;
            _lookupService = lookupService;
        }

        // Methods SetCurrentAnbarCode, GetCurrentAnbarCode, GetItemQuantity, GetCartItem, SetCustomer remain unchanged
        public void SetCurrentAnbarCode(int anbarCode)
        {
            if (CurrentAnbarCode != anbarCode && Items.Any())
            {
                Console.WriteLine($"Warning: AnbarCode changed from {CurrentAnbarCode} to {anbarCode} while cart has items.");
            }
            CurrentAnbarCode = anbarCode;
        }
        public int? GetCurrentAnbarCode() => CurrentAnbarCode;
        public decimal GetItemQuantity(string itemCode, int unitCode) => Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode)?.Quantity ?? 0;
        public CartItem? GetCartItem(string itemCode, int unitCode) => Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
        public void SetCustomer(VISITOR_CUSTOMERS? customer)
        {
            if (CurrentCustomer != customer)
            {
                if (customer != null && CurrentCustomer != null && Items.Any())
                {
                    ClearCartInternal();
                }
                CurrentCustomer = customer;
                NotifyCartChanged();
            }
        }


        // --- AddItem method updated ---
        public void AddItem(
            ItemDisplayDto item,
            decimal quantity,
            int unitCode,
            int anbarCode,
            List<TCOD_VAHEDS>? availableUnits,
            decimal? priceOverride = null,
            double? discountPercent = null) // <<< Added discountPercent parameter
        {
            if (item == null || quantity <= 0) return;
            if (anbarCode <= 0)
            {
                _logger?.LogError("Attempted to add item {ItemCode} with invalid AnbarCode {AnbarCode}", item.CODE, anbarCode);
                return;
            }

            var selectedUnit = availableUnits?.FirstOrDefault(u => u.CODE == unitCode);
            string? selectedUnitName = selectedUnit?.NAMES ?? item.VahedName;
            decimal pricePerUnit = priceOverride ?? item.MABL_F; // Use override or default price

            // Find existing item based on ItemCode, UnitCode, AND AnbarCode
            var existingItem = Items.FirstOrDefault(i => i.ItemCode == item.CODE && i.SelectedUnitCode == unitCode && i.AnbarCode == anbarCode);

            if (existingItem != null)
            {
                // Item exists: Update quantity, price, and discount
                existingItem.Quantity = quantity;
                existingItem.PricePerUnit = pricePerUnit;
                existingItem.DiscountPercent = discountPercent; // <<< Update discount
                _logger?.LogInformation("AddItem (Updating existing): Item {ItemCode}, Unit {UnitCode}, Anbar {AnbarCode}, New Qty {Qty}, New Price {Price}, New Disc {Disc}%",
                                         item.CODE, unitCode, anbarCode, quantity, pricePerUnit, discountPercent);
            }
            else
            {
                // Create new item
                var newItem = new CartItem
                {
                    SourceItem = item,
                    ItemCode = item.CODE,
                    ItemName = item.NAME,
                    Quantity = quantity,
                    SelectedUnitCode = unitCode,
                    SelectedUnitName = selectedUnitName,
                    PricePerUnit = pricePerUnit,
                    AnbarCode = anbarCode,
                    DiscountPercent = discountPercent // <<< Set discount
                };
                Items.Add(newItem);
                _logger?.LogInformation("AddItem (New): Item {ItemCode}, Unit {UnitCode}, Anbar {AnbarCode}, Qty {Qty}, Price {Price}, Disc {Disc}%",
                                         item.CODE, unitCode, anbarCode, quantity, pricePerUnit, discountPercent);
            }
            NotifyCartChanged();
        }

        public void RemoveItem(string itemCode, int unitCode)
        {
            // Assuming deletion doesn't need AnbarCode specificity,
            // otherwise, it needs to be passed and included in the Where clause.
            var itemsToRemove = Items.Where(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode).ToList();
            if (itemsToRemove.Any())
            {
                foreach (var item in itemsToRemove)
                {
                    Items.Remove(item);
                    _logger?.LogInformation("RemoveItem: Item {ItemCode}, Unit {UnitCode}, Anbar {AnbarCode}", item.ItemCode, item.SelectedUnitCode, item.AnbarCode);
                }
                NotifyCartChanged();
            }
        }


        // --- UpdateQuantity method updated ---
        public void UpdateQuantity(
            string itemCode,
            int unitCode,
            decimal newQuantity,
            decimal? priceOverride = null,
            double? discountPercent = null) // <<< Added discountPercent parameter
        {
            // Find item based on ItemCode and UnitCode.
            // TODO: Consider adding AnbarCode here if one item can be added from multiple warehouses distinctly.
            var itemToUpdate = Items.FirstOrDefault(i => i.ItemCode == itemCode && i.SelectedUnitCode == unitCode);
            if (itemToUpdate != null)
            {
                if (newQuantity > 0)
                {
                    itemToUpdate.Quantity = newQuantity;
                    if (priceOverride.HasValue) // Update price if provided
                    {
                        itemToUpdate.PricePerUnit = priceOverride.Value;
                    }
                    // Update discount (allow setting it back to null/0 if discountPercent is null/0)
                    itemToUpdate.DiscountPercent = discountPercent;

                    _logger?.LogInformation("UpdateQuantity: Item {ItemCode}, Unit {UnitCode}, Anbar {AnbarCode}, New Qty {Qty}, New Price {Price}, New Disc {Disc}%",
                                             itemToUpdate.ItemCode, itemToUpdate.SelectedUnitCode, itemToUpdate.AnbarCode, newQuantity, itemToUpdate.PricePerUnit, itemToUpdate.DiscountPercent);
                }
                else
                {
                    // Remove item if quantity is zero or less
                    Items.Remove(itemToUpdate);
                    _logger?.LogInformation("UpdateQuantity (Removed): Item {ItemCode}, Unit {UnitCode}, Anbar {AnbarCode}",
                                             itemToUpdate.ItemCode, itemToUpdate.SelectedUnitCode, itemToUpdate.AnbarCode);
                }
                NotifyCartChanged();
            }
            else
            {
                _logger?.LogWarning("UpdateQuantity: Item not found {ItemCode}, Unit {UnitCode}", itemCode, unitCode);
            }
        }

        // --- GetTotal method (Calculates total AFTER discount) ---
        public decimal GetTotal() => Items.Sum(item => item.TotalPriceAfterDiscount);

        // --- GetTotalDiscountAmount method (Calculates total discount amount) ---
        public decimal GetTotalDiscountAmount() => Items.Sum(item => item.LineDiscountAmount);

        // --- <<< NEW Method: GetTotalAmountBeforeDiscount >>> ---
        /// <summary>
        /// Calculates the sum of (Quantity * PricePerUnit) for all items, before any discount.
        /// </summary>
        /// <returns>Total amount before discount.</returns>
        public decimal GetTotalAmountBeforeDiscount() => Items.Sum(item => item.TotalPriceBeforeDiscount);
        // --- <<< End NEW Method >>> ---

        public void ClearCart()
        {
            Items.Clear();
            CurrentCustomer = null;
            NotifyCartChanged();
            _logger?.LogInformation("ClearCart: Cart cleared.");
        }

        private void ClearCartInternal() => Items.Clear();
        private void NotifyCartChanged() => CartChanged?.Invoke();
    }
}