import sys
import json
import pandas as pd

def export_to_excel(data, output_file="books.xlsx"):
    df = pd.DataFrame(data)
    df.to_excel(output_file, index=False, engine="openpyxl")
    print(f"Данные сохранены в {output_file}")

if __name__ == "__main__":
    json_file_path = sys.argv[1]
    with open(json_file_path, "r", encoding="utf-8") as file:
        books_data = json.load(file)
    export_to_excel(books_data)
