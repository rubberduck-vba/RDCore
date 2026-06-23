# 3.3.0 Operator Expressions

An _operator_ consists of a _bound expression node_ that yields a deterministic _result_ given one or more _operand_ inputs.

- An operator that accepts a single input is a **unary operator**;
- An operator that accepts two inputs is a **binary operator**;
- An operator that accepts three inputs is a **ternary operator**.

> 🧩 RD-VBA does not currently define any _ternary operators_.

- All _unary operators_ are _prefix_, with the operator token appearing _before_ its operand;
- All _binary operators_ are _infix_, with a _left_ and a _right_ operand and the operator token between them;
- _ternary operators_ are **undefined in RD-VBA** and should never be introduced in the _language core_.

All operators ultimately inherit `BoundNode`, which represents any type of AST node.


---
 V I V A T 🩷 C U C U M I S ™  

---

<p align="center">
<img alt="Logo™ 9562-7303 Québec inc." src="../images/vector-ducky.svg" style="width:200px; margin-top:72px;" /><br/>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/></small>
</p>

